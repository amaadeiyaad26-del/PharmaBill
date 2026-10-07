using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services.Reconciliation;

public sealed class ReconciliationService(IUnitOfWork unitOfWork) : IReconciliationService
{
	private sealed record OpenDocument(Guid Id, string Type, string DocumentNo, decimal Balance);

	private const double FuzzyCustomerThreshold = 0.72;

	private static readonly Regex GstinPattern = new Regex("\\b\\d{2}[A-Z]{5}\\d{4}[A-Z][A-Z0-9]Z[A-Z0-9]\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public async Task<ReconciliationResult> MatchAsync(IReadOnlyList<BankStatementEntry> bankLines, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(bankLines, "bankLines");
		PharmaBillDbContext context = unitOfWork.Context;
		List<Customer> customers = await (from customer in context.Customers.AsNoTracking()
			where customer.IsActive
			select customer).ToListAsync(cancellationToken);
		List<Receipt> receipts = await (from receipt in context.Receipts.AsNoTracking()
			where receipt.CustomerId.HasValue
			select receipt).ToListAsync(cancellationToken);
		Dictionary<Guid, List<OpenDocument>> openByCustomer = BuildOpenDocuments(customers, await (from invoice in context.WholesaleInvoices.AsNoTracking()
			where invoice.Status == "Posted"
			select invoice).ToListAsync(cancellationToken), receipts, await (from entry in context.CustomerLedgerEntries.AsNoTracking()
			where entry.EntryType.Contains("CreditNote")
			select entry).ToListAsync(cancellationToken));
		List<ReconciliationMatch> list = new List<ReconciliationMatch>(bankLines.Count);
		HashSet<Guid> hashSet = new HashSet<Guid>();
		foreach (BankStatementEntry item in bankLines.Where((BankStatementEntry entry) => entry.DepositAmount > 0m))
		{
			ReconciliationMatch reconciliationMatch = MatchLine(item, customers, receipts, openByCustomer, hashSet);
			list.Add(reconciliationMatch);
			foreach (MatchedDocument matchedDocument in reconciliationMatch.MatchedDocuments)
			{
				hashSet.Add(matchedDocument.DocumentId);
			}
		}
		return new ReconciliationResult(list, list.Count((ReconciliationMatch item) => item.Confidence == ReconciliationConfidence.High), list.Count((ReconciliationMatch item) => item.Confidence == ReconciliationConfidence.Medium), list.Count((ReconciliationMatch item) => item.Confidence == ReconciliationConfidence.Low), list.Count((ReconciliationMatch item) => item.MatchedDocuments.Count == 0));
	}

	public async Task<BatchSettleResult> BatchSettleConfirmedMatchesAsync(IReadOnlyList<ReconciliationMatch> confirmedMatches, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(confirmedMatches, "confirmedMatches");
		if (userId == Guid.Empty)
		{
			throw new UnauthorizedAccessException("Sign in before posting reconciliation receipts.");
		}
		ReconciliationMatch[] eligible = confirmedMatches.Where((ReconciliationMatch reconciliationMatch) =>
		{
			bool flag = reconciliationMatch.IsConfirmed && reconciliationMatch.CustomerId.HasValue && reconciliationMatch.MatchedDocuments.Count > 0;
			if (flag)
			{
				ReconciliationConfidence confidence = reconciliationMatch.Confidence;
				bool flag2 = (uint)(confidence - 1) <= 1u;
				flag = flag2;
			}
			return flag;
		}).ToArray();
		if (eligible.Length == 0)
		{
			return new BatchSettleResult(0, 0m, "No confirmed High/Medium matches to settle.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		BatchSettleResult result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			int posted = 0;
			decimal total = 0m;
			ReconciliationMatch[] array = eligible;
			foreach (ReconciliationMatch match in array)
			{
				Guid customerId = match.CustomerId.Value;
				decimal remaining = match.BankLine.DepositAmount;
				foreach (MatchedDocument item in match.MatchedDocuments.Where((MatchedDocument item) => item.DocumentType == "Invoice"))
				{
					if (!(remaining <= 0m))
					{
						decimal amount = Math.Min(remaining, item.BalanceAmount);
						if (!(amount <= 0m))
						{
							await PostReceiptCoreAsync(context, customerId, item.DocumentId, amount, match.BankLine, userId, cancellationToken);
							remaining -= amount;
							total += amount;
							posted++;
						}
						continue;
					}
					break;
				}
				if (remaining > 0.01m)
				{
					await PostReceiptCoreAsync(context, customerId, null, remaining, match.BankLine, userId, cancellationToken);
					total += remaining;
					posted++;
				}
			}
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = new BatchSettleResult(posted, decimal.Round(total, 2, MidpointRounding.AwayFromZero), $"Posted {posted} receipt(s) totalling ₹{total:0.00}.");
		}
		return result;
	}

	private static ReconciliationMatch MatchLine(BankStatementEntry line, IReadOnlyList<Customer> customers, IReadOnlyList<Receipt> receipts, IReadOnlyDictionary<Guid, List<OpenDocument>> openByCustomer, HashSet<Guid> claimedDocumentIds)
	{
		string reference = NormalizeRef(line.ReferenceOrUtr);
		if (!string.IsNullOrEmpty(reference))
		{
			Guid? guid = receipts.FirstOrDefault((Receipt receipt) => EqualsRef(receipt.ReferenceNumber, reference) || EqualsRef(receipt.ChequeNumber, reference))?.CustomerId;
			if (guid.HasValue)
			{
				Guid voucherCustomerId = guid.GetValueOrDefault();
				Customer customer = customers.FirstOrDefault((Customer item) => item.Id == voucherCustomerId);
				return CreateMatch(line, voucherCustomerId, customer?.Name, Array.Empty<MatchedDocument>(), 95.0, "Direct UTR/cheque match against recorded payment voucher.");
			}
		}
		foreach (string token in ExtractReferenceTokens(line.Narration))
		{
			Guid? guid = receipts.FirstOrDefault((Receipt receipt) => EqualsRef(receipt.ReferenceNumber, token) || EqualsRef(receipt.ChequeNumber, token))?.CustomerId;
			if (guid.HasValue)
			{
				Guid voucherCustomerId2 = guid.GetValueOrDefault();
				Customer customer2 = customers.FirstOrDefault((Customer item) => item.Id == voucherCustomerId2);
				return CreateMatch(line, voucherCustomerId2, customer2?.Name, Array.Empty<MatchedDocument>(), 92.0, "Narration UTR/cheque matched a recorded payment voucher.");
			}
		}
		(Guid, string, double)? tuple = ResolveCustomer(line.Narration, customers);
		if (!tuple.HasValue)
		{
			return CreateMatch(line, null, null, Array.Empty<MatchedDocument>(), 0.0, "No customer or voucher match.");
		}
		var (guid2, customerName, num) = tuple.Value;
		if (!openByCustomer.TryGetValue(guid2, out List<OpenDocument> value) || value.Count == 0)
		{
			return CreateMatch(line, guid2, customerName, Array.Empty<MatchedDocument>(), ClampPercent(num * 100.0 * 0.7), "Customer matched from narration; no open invoices/credit notes.");
		}
		List<OpenDocument> list = value.Where((OpenDocument doc) => !claimedDocumentIds.Contains(doc.Id)).ToList();
		int depositPaise = ToPaise(line.DepositAmount);
		OpenDocument openDocument = list.FirstOrDefault((OpenDocument doc) => ToPaise(doc.Balance) == depositPaise);
		if ((object)openDocument != null)
		{
			double percent = ((num >= 0.9) ? 95.0 : 90.0);
			return CreateMatch(line, guid2, customerName, new _003C_003Ez__ReadOnlySingleElementList<MatchedDocument>(ToMatched(openDocument)), percent, "Customer matched; single open document equals deposit.");
		}
		List<OpenDocument> list2 = FindSubset(list, depositPaise);
		if (list2.Count > 0)
		{
			double percent2 = ((num >= 0.85) ? 88.0 : 78.0);
			return CreateMatch(line, guid2, customerName, list2.Select(ToMatched).ToArray(), percent2, $"Customer matched; subset of {list2.Count} open document(s) equals deposit.");
		}
		return CreateMatch(line, guid2, customerName, Array.Empty<MatchedDocument>(), ClampPercent(num * 100.0 * 0.65), "Customer matched from narration; deposit does not equal any open combination.");
	}

	private static (Guid CustomerId, string Name, double Score)? ResolveCustomer(string narration, IReadOnlyList<Customer> customers)
	{
		if (string.IsNullOrWhiteSpace(narration) || customers.Count == 0)
		{
			return null;
		}
		Match gstin = GstinPattern.Match(narration);
		if (gstin.Success)
		{
			Customer customer = customers.FirstOrDefault((Customer customer3) => !string.IsNullOrWhiteSpace(customer3.Gstin) && string.Equals(customer3.Gstin.Trim(), gstin.Value, StringComparison.OrdinalIgnoreCase));
			if (customer != null)
			{
				return (customer.Id, customer.Name, 1.0);
			}
		}
		double num = 0.0;
		Customer customer2 = null;
		foreach (Customer customer3 in customers)
		{
			double num2 = Math.Max(FuzzyMatcher.Similarity(narration, customer3.Name), TokenOverlap(narration, customer3.Name));
			if (num2 > num)
			{
				num = num2;
				customer2 = customer3;
			}
		}
		if (customer2 == null || !(num >= 0.72))
		{
			return null;
		}
		return (customer2.Id, customer2.Name, num);
	}

	private static double TokenOverlap(string narration, string name)
	{
		string[] left = FuzzyMatcher.Normalize(narration).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		string[] array = FuzzyMatcher.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (left.Length == 0 || array.Length == 0)
		{
			return 0.0;
		}
		return (double)array.Count((string token) => token.Length >= 3 && left.Any((string word) => word.Contains(token, StringComparison.Ordinal) || token.Contains(word, StringComparison.Ordinal))) / (double)array.Length;
	}

	private static Dictionary<Guid, List<OpenDocument>> BuildOpenDocuments(IReadOnlyList<Customer> customers, IReadOnlyList<WholesaleInvoice> invoices, IReadOnlyList<Receipt> receipts, IReadOnlyList<CustomerLedgerEntry> creditNotes)
	{
		Dictionary<Guid, List<OpenDocument>> dictionary = customers.ToDictionary((Customer customer) => customer.Id, (Customer _) => new List<OpenDocument>());
		foreach (WholesaleInvoice invoice in invoices)
		{
			decimal val = receipts.Where((Receipt receipt) => receipt.WholesaleInvoiceId == invoice.Id).Sum((Receipt receipt) => receipt.Amount);
			decimal num = Math.Max(invoice.PaidAmount, val);
			decimal num2 = creditNotes.Where((CustomerLedgerEntry entry) => entry.CustomerId == invoice.CustomerId && (entry.ReferenceId == invoice.Id || (entry.Notes?.Contains(invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase) ?? false))).Sum((CustomerLedgerEntry entry) => entry.Credit);
			decimal num3 = Math.Max(0m, invoice.TotalAmount - num - num2);
			if (!(num3 <= 0m) && dictionary.ContainsKey(invoice.CustomerId))
			{
				dictionary[invoice.CustomerId].Add(new OpenDocument(invoice.Id, "Invoice", invoice.InvoiceNo, num3));
			}
		}
		foreach (CustomerLedgerEntry item in creditNotes.Where((CustomerLedgerEntry entry) => entry.Credit > 0m))
		{
			if (dictionary.ContainsKey(item.CustomerId))
			{
				dictionary[item.CustomerId].Add(new OpenDocument(item.Id, "CreditNote", item.ReferenceNo ?? "CN", item.Credit));
			}
		}
		return dictionary;
	}

	private static List<OpenDocument> FindSubset(IReadOnlyList<OpenDocument> documents, int targetPaise)
	{
		OpenDocument[] array = (from doc in documents
			orderby doc.Type == "Invoice" descending, doc.Balance descending
			select doc).Take(16).ToArray();
		if (array.Length == 0 || targetPaise <= 0)
		{
			return new List<OpenDocument>();
		}
		int num = array.Length;
		int num2 = 1 << num;
		int num3 = -1;
		int num4 = int.MaxValue;
		for (int num5 = 1; num5 < num2; num5++)
		{
			int num6 = 0;
			int num7 = 0;
			for (int num8 = 0; num8 < num; num8++)
			{
				if ((num5 & (1 << num8)) != 0)
				{
					num6 += ToPaise(array[num8].Balance);
					num7++;
					if (num6 > targetPaise)
					{
						break;
					}
				}
			}
			if (num6 == targetPaise && num7 < num4)
			{
				num4 = num7;
				num3 = num5;
			}
		}
		if (num3 < 0)
		{
			return new List<OpenDocument>();
		}
		List<OpenDocument> list = new List<OpenDocument>();
		for (int num9 = 0; num9 < num; num9++)
		{
			if ((num3 & (1 << num9)) != 0)
			{
				list.Add(array[num9]);
			}
		}
		return list;
	}

	private async Task PostReceiptCoreAsync(PharmaBillDbContext context, Guid customerId, Guid? wholesaleInvoiceId, decimal amount, BankStatementEntry bankLine, Guid userId, CancellationToken cancellationToken)
	{
		decimal rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
		if (rounded <= 0m)
		{
			return;
		}
		if (wholesaleInvoiceId.HasValue)
		{
			WholesaleInvoice invoice = await context.WholesaleInvoices.SingleAsync((WholesaleInvoice item) => item.Id == ((Guid?)wholesaleInvoiceId).Value, cancellationToken);
			decimal valueOrDefault = (await context.Receipts.Where((Receipt receipt2) => receipt2.WholesaleInvoiceId == invoice.Id).SumAsync((Expression<Func<Receipt, decimal?>>)((Receipt receipt2) => receipt2.Amount), cancellationToken)).GetValueOrDefault();
			decimal num = Math.Max(invoice.PaidAmount, valueOrDefault);
			decimal num2 = invoice.TotalAmount - num;
			if (rounded > num2 + 0.01m)
			{
				rounded = Math.Max(0m, num2);
			}
		}
		DateTime utcNow = DateTime.UtcNow;
		Receipt receipt = new Receipt
		{
			CustomerId = customerId,
			WholesaleInvoiceId = wholesaleInvoiceId,
			ReceiptNo = $"RCPT-{utcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 30),
			ReceiptAtUtc = bankLine.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
			Amount = rounded,
			PaymentMethod = "BankTransfer",
			ReferenceNumber = NullIfWhiteSpace(bankLine.ReferenceOrUtr),
			Notes = "Bank reconciliation: " + Trim(bankLine.Narration, 120)
		};
		context.Receipts.Add(receipt);
		context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
		{
			CustomerId = customerId,
			EntryAtUtc = receipt.ReceiptAtUtc,
			EntryType = ((!wholesaleInvoiceId.HasValue) ? "OnAccountReceipt" : "Receipt"),
			ReferenceId = receipt.Id,
			ReferenceNo = receipt.ReceiptNo,
			Credit = receipt.Amount,
			Notes = receipt.Notes
		});
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			ActionAtUtc = utcNow,
			Action = "BankReconciliationReceiptPosted",
			EntityName = "Receipt",
			EntityId = receipt.Id,
			Details = $"Recon deposit {bankLine.DepositAmount:0.00}; posted {rounded:0.00}."
		});
	}

	private static ReconciliationMatch CreateMatch(BankStatementEntry line, Guid? customerId, string? customerName, IReadOnlyList<MatchedDocument> documents, double percent, string reason)
	{
		double num = ClampPercent(percent);
		ReconciliationConfidence reconciliationConfidence = ((num >= 90.0) ? ReconciliationConfidence.High : ((num >= 70.0) ? ReconciliationConfidence.Medium : ReconciliationConfidence.Low));
		return new ReconciliationMatch(line, customerId, customerName, documents, num, reconciliationConfidence, reason, reconciliationConfidence == ReconciliationConfidence.High);
	}

	private static MatchedDocument ToMatched(OpenDocument document)
	{
		return new MatchedDocument(document.Id, document.Type, document.DocumentNo, document.Balance);
	}

	private static int ToPaise(decimal amount)
	{
		return (int)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
	}

	private static double ClampPercent(double value)
	{
		return Math.Clamp(Math.Round(value, 1), 0.0, 100.0);
	}

	private static string NormalizeRef(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return Regex.Replace(value.Trim().ToUpperInvariant(), "[\\s\\-_/]", string.Empty);
		}
		return string.Empty;
	}

	private static bool EqualsRef(string? left, string right)
	{
		string text = NormalizeRef(left);
		if (text.Length >= 4)
		{
			return text == right;
		}
		return false;
	}

	private static IEnumerable<string> ExtractReferenceTokens(string narration)
	{
		foreach (Match item in Regex.Matches(narration ?? string.Empty, "\\b[A-Z0-9]{6,22}\\b", RegexOptions.IgnoreCase))
		{
			yield return NormalizeRef(item.Value);
		}
	}

	private static string? NullIfWhiteSpace(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}

	private static string Trim(string? value, int max)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			if (value.Length > max)
			{
				return value.Trim().Substring(0, max);
			}
			return value.Trim();
		}
		return string.Empty;
	}
}
