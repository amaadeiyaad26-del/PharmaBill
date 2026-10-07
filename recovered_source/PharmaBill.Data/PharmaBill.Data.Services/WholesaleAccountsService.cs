using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class WholesaleAccountsService(IUnitOfWork unitOfWork)
{
	public async Task<Receipt> RecordReceiptAsync(Guid customerId, Guid? wholesaleInvoiceId, decimal amount, string paymentMethod, string? referenceNumber, string? chequeNumber, DateOnly? chequeDate, string? bankName, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (amount <= 0m || string.IsNullOrWhiteSpace(paymentMethod))
		{
			throw new InvalidOperationException("Receipt amount must be positive and payment method is required.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		Customer customer = (await context.Customers.SingleOrDefaultAsync((Customer item) => item.Id == customerId && item.IsActive, cancellationToken)) ?? throw new InvalidOperationException("Select an active customer.");
		WholesaleInvoice invoice = null;
		if (wholesaleInvoiceId.HasValue)
		{
			invoice = (await context.WholesaleInvoices.SingleOrDefaultAsync((WholesaleInvoice item) => item.Id == ((Guid?)wholesaleInvoiceId).Value && item.CustomerId == customerId && item.Status == "Posted", cancellationToken)) ?? throw new InvalidOperationException("Select an unpaid invoice for this customer.");
			decimal valueOrDefault = (await context.Receipts.Where((Receipt receipt2) => receipt2.WholesaleInvoiceId == invoice.Id).SumAsync((Expression<Func<Receipt, decimal?>>)((Receipt receipt2) => receipt2.Amount), cancellationToken)).GetValueOrDefault();
			if (Math.Max(invoice.PaidAmount, valueOrDefault) + amount > invoice.TotalAmount)
			{
				throw new InvalidOperationException("Receipt exceeds the remaining invoice balance.");
			}
		}
		string method = paymentMethod.Trim();
		if (method.Equals("Cheque", StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(chequeNumber) || !chequeDate.HasValue || string.IsNullOrWhiteSpace(bankName)))
		{
			throw new InvalidOperationException("Cheque receipts need cheque number, cheque date and bank name.");
		}
		Receipt result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			Receipt receipt = new Receipt
			{
				CustomerId = customer.Id,
				WholesaleInvoiceId = invoice?.Id,
				ReceiptNo = $"RCPT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 30),
				ReceiptAtUtc = DateTime.UtcNow,
				Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
				PaymentMethod = method,
				ReferenceNumber = NullIfWhiteSpace(referenceNumber),
				ChequeNumber = NullIfWhiteSpace(chequeNumber),
				ChequeDate = chequeDate,
				BankName = NullIfWhiteSpace(bankName),
				Notes = ((invoice == null) ? "On-account receipt" : ("Against invoice " + invoice.InvoiceNo))
			};
			context.Receipts.Add(receipt);
			context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
			{
				CustomerId = customer.Id,
				EntryAtUtc = receipt.ReceiptAtUtc,
				EntryType = ((invoice == null) ? "OnAccountReceipt" : "Receipt"),
				ReferenceId = receipt.Id,
				ReferenceNo = receipt.ReceiptNo,
				Credit = receipt.Amount,
				Notes = receipt.Notes
			});
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				ActionAtUtc = receipt.ReceiptAtUtc,
				Action = "WholesaleReceiptRecorded",
				EntityName = "Receipt",
				EntityId = receipt.Id,
				Details = $"Customer {customer.Name}; amount {receipt.Amount}; method {method}."
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = receipt;
		}
		return result;
	}

	public async Task<Receipt> RecordSupplierPaymentAsync(Guid supplierId, decimal amount, string paymentMethod, string? referenceNumber, string? chequeNumber, DateOnly? chequeDate, string? bankName, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (amount <= 0m || string.IsNullOrWhiteSpace(paymentMethod))
		{
			throw new InvalidOperationException("Payment amount must be positive and method is required.");
		}
		PharmaBillDbContext context = unitOfWork.Context;
		Supplier supplier = (await context.Suppliers.SingleOrDefaultAsync((Supplier item) => item.Id == supplierId && item.IsActive, cancellationToken)) ?? throw new InvalidOperationException("Select an active supplier.");
		string method = paymentMethod.Trim();
		if (method.Equals("Cheque", StringComparison.OrdinalIgnoreCase) && (string.IsNullOrWhiteSpace(chequeNumber) || !chequeDate.HasValue || string.IsNullOrWhiteSpace(bankName)))
		{
			throw new InvalidOperationException("Cheque payments need cheque number, cheque date and bank name.");
		}
		Receipt result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			DateTime utcNow = DateTime.UtcNow;
			Receipt payment = new Receipt
			{
				SupplierId = supplier.Id,
				ReceiptNo = $"PAY-{utcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}".Substring(0, 30),
				ReceiptAtUtc = utcNow,
				Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
				PaymentMethod = method,
				ReferenceNumber = NullIfWhiteSpace(referenceNumber),
				ChequeNumber = NullIfWhiteSpace(chequeNumber),
				ChequeDate = chequeDate,
				BankName = NullIfWhiteSpace(bankName),
				Notes = "Supplier payment to " + supplier.Name
			};
			context.Receipts.Add(payment);
			context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
			{
				SupplierId = supplier.Id,
				EntryAtUtc = utcNow,
				EntryType = "SupplierPayment",
				ReferenceId = payment.Id,
				ReferenceNo = payment.ReceiptNo,
				Debit = payment.Amount,
				Notes = payment.Notes
			});
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				ActionAtUtc = utcNow,
				Action = "WholesaleSupplierPaymentRecorded",
				EntityName = "Receipt",
				EntityId = payment.Id,
				Details = $"Supplier {supplier.Name}; amount {payment.Amount}; method {method}."
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = payment;
		}
		return result;
	}

	public async Task<IReadOnlyList<LedgerRow>> GetCustomerLedgerAsync(Guid customerId, CancellationToken cancellationToken = default(CancellationToken))
	{
		Customer customer = (await unitOfWork.Context.Customers.AsNoTracking().SingleOrDefaultAsync((Customer item) => item.Id == customerId, cancellationToken)) ?? throw new InvalidOperationException("Customer not found.");
		List<CustomerLedgerEntry> list = await (from entry in unitOfWork.Context.CustomerLedgerEntries.AsNoTracking()
			where entry.CustomerId == customerId
			orderby entry.EntryAtUtc, entry.CreatedAtUtc
			select entry).ToListAsync(cancellationToken);
		decimal openingBalance = customer.OpeningBalance;
		List<LedgerRow> list2 = new List<LedgerRow>(list.Count + 1);
		if (customer.OpeningBalance != 0m)
		{
			list2.Add(new LedgerRow(DateTime.MinValue, "Opening balance", null, customer.OpeningBalance, 0m, openingBalance, null));
		}
		foreach (CustomerLedgerEntry item in list)
		{
			openingBalance += item.Debit - item.Credit;
			list2.Add(new LedgerRow(item.EntryAtUtc, item.EntryType, item.ReferenceNo, item.Debit, item.Credit, openingBalance, item.Notes));
		}
		return list2;
	}

	public async Task<IReadOnlyList<CustomerAgeing>> GetAgeingAsync(DateOnly asOfDate, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<Customer> customers = await (from customer2 in unitOfWork.Context.Customers.AsNoTracking()
			where customer2.IsActive
			select customer2).ToListAsync(cancellationToken);
		List<WholesaleInvoice> invoices = await (from wholesaleInvoice in unitOfWork.Context.WholesaleInvoices.AsNoTracking()
			where wholesaleInvoice.Status == "Posted"
			select wholesaleInvoice).ToListAsync(cancellationToken);
		List<Receipt> receipts = await (from receipt in unitOfWork.Context.Receipts.AsNoTracking()
			where receipt.CustomerId.HasValue
			select receipt).ToListAsync(cancellationToken);
		List<CustomerLedgerEntry> source = await (from entry in unitOfWork.Context.CustomerLedgerEntries.AsNoTracking()
			where entry.EntryType.Contains("CreditNote") || entry.EntryType.Contains("Cancellation")
			select entry).ToListAsync(cancellationToken);
		Dictionary<Guid, WholesaleInvoice[]> dictionary = (from wholesaleInvoice in invoices
			group wholesaleInvoice by wholesaleInvoice.CustomerId).ToDictionary((IGrouping<Guid, WholesaleInvoice> group) => group.Key, (IGrouping<Guid, WholesaleInvoice> group) => group.ToArray());
		List<CustomerAgeing> list = new List<CustomerAgeing>();
		foreach (Customer customer in customers)
		{
			decimal[] array = new decimal[4];
			if (dictionary.TryGetValue(customer.Id, out var value))
			{
				decimal val = receipts.Where((Receipt receipt) =>
				{
					Guid? customerId = receipt.CustomerId;
					Guid id = customer.Id;
					return customerId.HasValue && customerId.GetValueOrDefault() == id && !receipt.WholesaleInvoiceId.HasValue && DateOnly.FromDateTime(receipt.ReceiptAtUtc.ToLocalTime()) <= asOfDate;
				}).Sum((Receipt receipt) => receipt.Amount);
				foreach (WholesaleInvoice invoice in value.OrderBy((WholesaleInvoice item) => item.InvoiceAtUtc))
				{
					decimal val2 = receipts.Where((Receipt receipt) => receipt.WholesaleInvoiceId == invoice.Id && DateOnly.FromDateTime(receipt.ReceiptAtUtc.ToLocalTime()) <= asOfDate).Sum((Receipt receipt) => receipt.Amount);
					decimal num = Math.Max(invoice.PaidAmount, val2);
					decimal num2 = source.Where((CustomerLedgerEntry entry) =>
					{
						if (entry.CustomerId == customer.Id)
						{
							if (entry.ReferenceId == invoice.Id)
							{
								goto IL_0080;
							}
							if (entry.EntryType.Contains("CreditNote"))
							{
								string? notes = entry.Notes;
								if (notes != null && notes.Contains(invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase))
								{
									goto IL_0080;
								}
							}
						}
						return false;
						IL_0080:
						return DateOnly.FromDateTime(entry.EntryAtUtc.ToLocalTime()) <= asOfDate;
					}).Sum((CustomerLedgerEntry entry) => entry.Credit);
					decimal num3 = Math.Max(0m, invoice.TotalAmount - num - num2);
					decimal num4 = Math.Min(num3, val);
					num3 -= num4;
					val -= num4;
					if (!(num3 == 0m))
					{
						DateOnly dateOnly = DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()).AddDays(customer.CreditDays);
						int num5 = Math.Max(0, asOfDate.DayNumber - dateOnly.DayNumber);
						int num6;
						if (num5 <= 60)
						{
							num6 = ((num5 > 30) ? 1 : 0);
						}
						else
						{
							num6 = ((num5 > 90) ? 3 : 2);
						}
						array[num6] += num3;
					}
				}
			}
			decimal num7 = array.Sum();
			if (num7 > 0m)
			{
				list.Add(new CustomerAgeing(customer.Id, customer.Name, customer.Phone, array[0], array[1], array[2], array[3], num7));
			}
		}
		return list.OrderByDescending((CustomerAgeing item) => item.Total).ToArray();
	}

	public async Task<IReadOnlyList<CashBankBookRow>> GetCashBankBookAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (fromUtc.Kind != DateTimeKind.Utc || toUtcExclusive.Kind != DateTimeKind.Utc || toUtcExclusive <= fromUtc)
		{
			throw new ArgumentException("Cash/bank book period must be a non-empty UTC range.");
		}
		List<Receipt> receipts = await (from item in unitOfWork.Context.Receipts.AsNoTracking()
			where item.ReceiptAtUtc >= fromUtc && item.ReceiptAtUtc < toUtcExclusive
			select item).ToListAsync(cancellationToken);
		Dictionary<Guid, string> customers = await unitOfWork.Context.Customers.AsNoTracking().ToDictionaryAsync((Customer item) => item.Id, (Customer item) => item.Name, cancellationToken);
		Dictionary<Guid, string> suppliers = await unitOfWork.Context.Suppliers.AsNoTracking().ToDictionaryAsync((Supplier item) => item.Id, (Supplier item) => item.Name, cancellationToken);
		return (from item in receipts.Select((Receipt receipt) =>
			{
				DateTime receiptAtUtc = receipt.ReceiptAtUtc;
				string receiptNo = receipt.ReceiptNo;
				string party = (receipt.CustomerId.HasValue ? customers.GetValueOrDefault(receipt.CustomerId.Value, "Unknown customer") : (receipt.SupplierId.HasValue ? suppliers.GetValueOrDefault(receipt.SupplierId.Value, "Unknown supplier") : "Unassigned"));
				return new CashBankBookRow(receiptAtUtc, receiptNo, party, receipt.CustomerId.HasValue ? "In" : "Out", receipt.PaymentMethod, receipt.ChequeNumber ?? receipt.ReferenceNumber, receipt.Amount);
			})
			orderby item.AtUtc
			select item).ToArray();
	}

	public async Task<IReadOnlyList<LedgerRow>> GetSupplierLedgerAsync(Guid supplierId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!(await unitOfWork.Context.Suppliers.AnyAsync((Supplier item) => item.Id == supplierId, cancellationToken)))
		{
			throw new InvalidOperationException("Supplier not found.");
		}
		List<SupplierLedgerEntry> source = await (from entry in unitOfWork.Context.SupplierLedgerEntries.AsNoTracking()
			where entry.SupplierId == supplierId
			orderby entry.EntryAtUtc, entry.CreatedAtUtc
			select entry).ToListAsync(cancellationToken);
		decimal balance = 0m;
		return source.Select((SupplierLedgerEntry entry) =>
		{
			balance += entry.Credit - entry.Debit;
			return new LedgerRow(entry.EntryAtUtc, entry.EntryType, entry.ReferenceNo, entry.Debit, entry.Credit, balance, entry.Notes);
		}).ToArray();
	}

	public async Task<IReadOnlyList<(Guid SupplierId, string SupplierName, decimal Payable)>> GetSupplierPayablesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		List<Supplier> suppliers = await (from item in unitOfWork.Context.Suppliers.AsNoTracking()
			where item.IsActive
			select item).ToListAsync(cancellationToken);
		var entries = await (from item in unitOfWork.Context.SupplierLedgerEntries.AsNoTracking()
			group item by item.SupplierId into @group
			select new
			{
				SupplierId = @group.Key,
				Credit = @group.Sum((SupplierLedgerEntry item) => item.Credit),
				Debit = @group.Sum((SupplierLedgerEntry item) => item.Debit)
			}).ToDictionaryAsync(item => item.SupplierId, cancellationToken);
		return (from supplier in suppliers
			select (Id: supplier.Id, Name: supplier.Name, Math.Max(0m, (entries.GetValueOrDefault(supplier.Id)?.Credit - entries.GetValueOrDefault(supplier.Id)?.Debit).GetValueOrDefault())) into item
			where item.Item3 > 0m
			orderby item.Item3 descending
			select item).ToArray();
	}

	private static string? NullIfWhiteSpace(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}
}
