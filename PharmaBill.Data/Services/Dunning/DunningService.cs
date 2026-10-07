using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services.Dunning;

public sealed class DunningService(IUnitOfWork unitOfWork) : IDunningService
{
	public async Task<IReadOnlyList<CustomerCreditProfile>> GetCreditProfilesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<DunningReceivableRow> rows = await GetOpenReceivablesAsync(cancellationToken);
		List<Customer> customers = await (from customer in unitOfWork.Context.Customers.AsNoTracking()
			where customer.IsActive
			select customer).ToListAsync(cancellationToken);
		Dictionary<Guid, double> delays = await ComputeAveragePaymentDelaysAsync(cancellationToken);
		return (from customer in customers
			select BuildProfile(customer, rows, delays) into profile
			where profile.OutstandingAmount > 0m || profile.CreditLimit > 0m
			orderby profile.DefaultRiskScore descending, profile.OverdueAmount descending
			select profile).ToArray();
	}

	public async Task<CustomerCreditProfile?> GetCreditProfileAsync(Guid customerId, CancellationToken cancellationToken = default(CancellationToken))
	{
		Customer customer = await unitOfWork.Context.Customers.AsNoTracking().SingleOrDefaultAsync((Customer item) => item.Id == customerId, cancellationToken);
		if (customer == null)
		{
			return null;
		}
		DunningReceivableRow[] rows = (await GetOpenReceivablesAsync(cancellationToken)).Where((DunningReceivableRow row) => row.CustomerId == customerId).ToArray();
		return BuildProfile(customer, rows, await ComputeAveragePaymentDelaysAsync(cancellationToken));
	}

	public async Task<IReadOnlyList<DunningReceivableRow>> GetOpenReceivablesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		PharmaBillDbContext context = unitOfWork.Context;
		Dictionary<Guid, Customer> customers = await (from customer in context.Customers.AsNoTracking()
			where customer.IsActive
			select customer).ToDictionaryAsync((Customer customer) => customer.Id, cancellationToken);
		List<WholesaleInvoice> invoices = await (from wholesaleInvoice in context.WholesaleInvoices.AsNoTracking()
			where wholesaleInvoice.Status == "Posted"
			select wholesaleInvoice).ToListAsync(cancellationToken);
		List<Receipt> receipts = await (from receipt in context.Receipts.AsNoTracking()
			where receipt.CustomerId.HasValue
			select receipt).ToListAsync(cancellationToken);
		List<CustomerLedgerEntry> creditEntries = await (from entry in context.CustomerLedgerEntries.AsNoTracking()
			where entry.EntryType.Contains("CreditNote") || entry.EntryType.Contains("Cancellation")
			select entry).ToListAsync(cancellationToken);
		Dictionary<Guid, double> dictionary = await ComputeAveragePaymentDelaysAsync(cancellationToken);
		List<DunningReceivableRow> list = new List<DunningReceivableRow>();
		foreach (WholesaleInvoice invoice in invoices)
		{
			if (customers.TryGetValue(invoice.CustomerId, out var value))
			{
				decimal val = receipts.Where((Receipt receipt) => receipt.WholesaleInvoiceId == invoice.Id).Sum((Receipt receipt) => receipt.Amount);
				decimal num = Math.Max(invoice.PaidAmount, val);
				decimal num2 = creditEntries.Where((CustomerLedgerEntry entry) => entry.CustomerId == invoice.CustomerId && (entry.ReferenceId == invoice.Id || (entry.Notes?.Contains(invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase) ?? false))).Sum((CustomerLedgerEntry entry) => entry.Credit);
				decimal num3 = Math.Max(0m, invoice.TotalAmount - num - num2);
				if (!(num3 <= 0m))
				{
					DateOnly invoiceDate = DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime());
					int num4 = Math.Max(0, value.CreditDays);
					DateOnly dueDate = invoiceDate.AddDays(num4);
					int num5 = today.DayNumber - dueDate.DayNumber;
					double valueOrDefault = dictionary.GetValueOrDefault(value.Id);
					DunningTier tier = ClassifyTier(num5);
					DefaultRiskScore riskScore = ClassifyRisk(valueOrDefault, num5, num3, value.CreditLimit);
					DateOnly optimalReminderDate = OptimalReminderDate(dueDate, tier, valueOrDefault, today);
					list.Add(new DunningReceivableRow(value.Id, value.Name, value.Phone, invoice.Id, invoice.InvoiceNo, invoiceDate, dueDate, num3, Math.Max(0, num5), tier, optimalReminderDate, riskScore, value.CreditLimit, num4, valueOrDefault));
				}
			}
		}
		return (from row in list
			orderby row.RiskScore descending, row.OutstandingAmount descending, row.DaysPastDue descending
			select row).ToArray();
	}

	public async Task<CreditGateAlert> EvaluateCreditGateAsync(Guid customerId, decimal additionalInvoiceAmount = 0m, CancellationToken cancellationToken = default(CancellationToken))
	{
		CustomerCreditProfile customerCreditProfile = await GetCreditProfileAsync(customerId, cancellationToken);
		if ((object)customerCreditProfile == null)
		{
			return new CreditGateAlert(IsBlocked: false, ExceedsCreditLimit: false, HasActiveT4: false, string.Empty, null);
		}
		decimal num = customerCreditProfile.OutstandingAmount + Math.Max(0m, additionalInvoiceAmount);
		bool flag = customerCreditProfile.CreditLimit > 0m && num > customerCreditProfile.CreditLimit;
		bool hasActiveT = customerCreditProfile.HasActiveT4;
		if (!flag && !hasActiveT)
		{
			return new CreditGateAlert(IsBlocked: false, ExceedsCreditLimit: false, HasActiveT4: false, string.Empty, customerCreditProfile);
		}
		List<string> list = new List<string>();
		if (flag)
		{
			list.Add($"Credit Limit Exceeded (limit ₹{customerCreditProfile.CreditLimit:0.00}, projected ₹{num:0.00})");
		}
		if (hasActiveT)
		{
			list.Add("Active T4 critical overdue — Auto-Credit-Lock");
		}
		list.Add("Admin Override Required");
		return new CreditGateAlert(IsBlocked: true, flag, hasActiveT, string.Join(" — ", list), customerCreditProfile);
	}

	public IReadOnlyList<DunningWhatsAppMessage> BuildWhatsAppReminders(IReadOnlyList<DunningReceivableRow> rows, string pharmacyName, string? paymentDetails)
	{
		ArgumentNullException.ThrowIfNull(rows, "rows");
		string payInfo = (string.IsNullOrWhiteSpace(paymentDetails) ? "Please share UTR / cheque details after payment." : paymentDetails.Trim());
		string store = (string.IsNullOrWhiteSpace(pharmacyName) ? "our pharmacy" : pharmacyName.Trim());
		return (from message in (from row in rows
				group row by row.CustomerId).Select((IGrouping<Guid, DunningReceivableRow> @group) =>
			{
				DunningReceivableRow dunningReceivableRow = @group.First();
				decimal value = @group.Sum((DunningReceivableRow row) => row.OutstandingAmount);
				string text = @group.Max((DunningReceivableRow row) => row.Tier) switch
				{
					DunningTier.T4_Critical => "firm", 
					DunningTier.T3_Overdue => "polite-firm", 
					_ => "polite", 
				};
				string value2 = string.Join("\n", from row in @group
					orderby row.DueDate
					select $"• {row.InvoiceNo} (due {row.DueDate:dd-MMM-yyyy}): ₹{row.OutstandingAmount:0.00}");
				string value3 = ((text == "firm") ? $"Dear {dunningReceivableRow.CustomerName}, this is an urgent collections notice from {store}." : $"Dear {dunningReceivableRow.CustomerName}, warm greetings from {store}.");
				string value4 = ((text == "firm") ? "Kindly clear the overdue balance within 48 hours to avoid credit hold on future supplies." : "Kindly arrange payment at your earliest convenience. We value your partnership.");
				string text2 = $"{value3}\n\nOutstanding: ₹{value:0.00}\n\nInvoices:\n{value2}\n\n{value4}\n\nPayment details: {payInfo}\n\nThank you.";
				string text3 = DigitsOnly(dunningReceivableRow.Phone);
				string waMeUrl = (string.IsNullOrEmpty(text3) ? string.Empty : ("https://wa.me/" + text3 + "?text=" + Uri.EscapeDataString(text2)));
				return new DunningWhatsAppMessage(dunningReceivableRow.CustomerId, dunningReceivableRow.CustomerName, dunningReceivableRow.Phone, text2, waMeUrl);
			})
			where !string.IsNullOrEmpty(message.WaMeUrl)
			select message).ToArray();
	}

	private static CustomerCreditProfile BuildProfile(Customer customer, IReadOnlyList<DunningReceivableRow> rows, IReadOnlyDictionary<Guid, double> delays)
	{
		DunningReceivableRow[] array = rows.Where((DunningReceivableRow row) => row.CustomerId == customer.Id).ToArray();
		decimal num = array.Sum((DunningReceivableRow row) => row.OutstandingAmount);
		decimal overdueAmount = array.Where((DunningReceivableRow row) => row.DaysPastDue > 0).Sum((DunningReceivableRow row) => row.OutstandingAmount);
		double valueOrDefault = delays.GetValueOrDefault(customer.Id);
		int daysPastDue = ((array.Length != 0) ? array.Max((DunningReceivableRow row) => row.DaysPastDue) : 0);
		DefaultRiskScore defaultRiskScore = ClassifyRisk(valueOrDefault, daysPastDue, overdueAmount, customer.CreditLimit);
		bool hasActiveT = array.Any((DunningReceivableRow row) => row.Tier == DunningTier.T4_Critical);
		bool exceedsCreditLimit = customer.CreditLimit > 0m && num > customer.CreditLimit;
		return new CustomerCreditProfile(customer.Id, customer.Name, customer.Phone, customer.Gstin, valueOrDefault, defaultRiskScore, customer.CreditLimit, customer.CreditDays, num, overdueAmount, exceedsCreditLimit, hasActiveT);
	}

	private async Task<Dictionary<Guid, double>> ComputeAveragePaymentDelaysAsync(CancellationToken cancellationToken)
	{
		PharmaBillDbContext context = unitOfWork.Context;
		List<WholesaleInvoice> invoices = await (from invoice in context.WholesaleInvoices.AsNoTracking()
			where invoice.Status == "Posted"
			select invoice).ToListAsync(cancellationToken);
		List<Receipt> receipts = await (from receipt in context.Receipts.AsNoTracking()
			where receipt.CustomerId.HasValue && receipt.WholesaleInvoiceId.HasValue
			select receipt).ToListAsync(cancellationToken);
		Dictionary<Guid, Customer> dictionary = await context.Customers.AsNoTracking().ToDictionaryAsync((Customer customer) => customer.Id, cancellationToken);
		Dictionary<Guid, List<double>> dictionary2 = new Dictionary<Guid, List<double>>();
		foreach (IGrouping<Guid, Receipt> group in from receipt in receipts
			group receipt by receipt.WholesaleInvoiceId.Value)
		{
			WholesaleInvoice wholesaleInvoice = invoices.FirstOrDefault((WholesaleInvoice item) => item.Id == group.Key);
			if (wholesaleInvoice != null && dictionary.TryGetValue(wholesaleInvoice.CustomerId, out var value))
			{
				DateTime dateTime = group.Max((Receipt receipt) => receipt.ReceiptAtUtc);
				DateOnly dateOnly = DateOnly.FromDateTime(wholesaleInvoice.InvoiceAtUtc.ToLocalTime()).AddDays(Math.Max(0, value.CreditDays));
				int num = DateOnly.FromDateTime(dateTime.ToLocalTime()).DayNumber - dateOnly.DayNumber;
				if (!dictionary2.TryGetValue(value.Id, out var value2))
				{
					value2 = new List<double>();
					dictionary2[value.Id] = value2;
				}
				value2.Add(num);
			}
		}
		return dictionary2.ToDictionary((KeyValuePair<Guid, List<double>> pair) => pair.Key, (KeyValuePair<Guid, List<double>> pair) => Math.Round(pair.Value.Average(), 1));
	}

	private static DunningTier ClassifyTier(int daysPastDue)
	{
		if (daysPastDue >= 0)
		{
			if (daysPastDue < 15)
			{
				if (daysPastDue >= 7)
				{
					return DunningTier.T3_Overdue;
				}
				return DunningTier.T2_Due;
			}
			return DunningTier.T4_Critical;
		}
		if (daysPastDue >= -2)
		{
			return DunningTier.T1_PreDue;
		}
		return DunningTier.None;
	}

	private static DateOnly OptimalReminderDate(DateOnly dueDate, DunningTier tier, double averageDelayDays, DateOnly today)
	{
		int num = (int)Math.Clamp(Math.Round(averageDelayDays), 0.0, 10.0);
		DateOnly dateOnly = tier switch
		{
			DunningTier.T1_PreDue => dueDate.AddDays(-2 - num / 2), 
			DunningTier.T2_Due => dueDate, 
			DunningTier.T3_Overdue => dueDate.AddDays(7), 
			DunningTier.T4_Critical => dueDate.AddDays(15), 
			_ => dueDate.AddDays(-2), 
		};
		if (!(dateOnly < today))
		{
			return dateOnly;
		}
		return today;
	}

	private static DefaultRiskScore ClassifyRisk(double averageDelayDays, int daysPastDue, decimal overdueAmount, decimal creditLimit)
	{
		if (daysPastDue >= 15 || averageDelayDays >= 12.0 || (creditLimit > 0m && overdueAmount > creditLimit))
		{
			return DefaultRiskScore.High;
		}
		if (daysPastDue >= 7 || averageDelayDays >= 5.0 || overdueAmount > 0m)
		{
			return DefaultRiskScore.Medium;
		}
		return DefaultRiskScore.Low;
	}

	private static string DigitsOnly(string? phone)
	{
		if (string.IsNullOrWhiteSpace(phone))
		{
			return string.Empty;
		}
		string text = new string(phone.Where(char.IsDigit).ToArray());
		if (text.Length < 8)
		{
			return string.Empty;
		}
		return text;
	}
}
