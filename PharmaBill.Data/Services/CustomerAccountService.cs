using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>What is owed, by age: 0-30, 31-60, 61-90 and over 90 days past the customer's credit days.</summary>
public sealed record AgeingBuckets(decimal Days0To30, decimal Days31To60, decimal Days61To90, decimal Over90)
{
	public decimal Total => Days0To30 + Days31To60 + Days61To90 + Over90;
}

public sealed record CustomerAccountSummary(Guid CustomerId, string Name, string? Phone, decimal CreditLimit, int CreditDays, decimal Outstanding, AgeingBuckets Ageing);

/// <summary>One line of the statement. <see cref="AtUtc"/> is null for the opening balance.</summary>
public sealed record AccountLedgerLine(DateTime? AtUtc, string Kind, string? Reference, decimal Debit, decimal Credit, decimal Balance, string? Notes);

public sealed record AccountBill(string Kind, Guid Id, string Number, DateTime AtUtc, decimal Total, decimal Paid, string Status)
{
	public bool IsRetail => Kind == CustomerAccountService.RetailBill;
}

public sealed record AccountPayment(DateTime AtUtc, string ReceiptNo, decimal Amount, string Mode, string? Reference, string? Notes);

public sealed record AccountCredit(DateTime AtUtc, string Kind, string Reference, decimal Amount, string? Notes);

public sealed record AccountNote(Guid Id, DateTime AtUtc, string Author, string Text);

public sealed record CustomerAccount(
	CustomerAccountSummary Summary,
	IReadOnlyList<AccountLedgerLine> Ledger,
	IReadOnlyList<AccountBill> Bills,
	IReadOnlyList<AccountPayment> Payments,
	IReadOnlyList<AccountCredit> Credits,
	IReadOnlyList<AccountNote> Notes);

/// <summary>
/// A customer's account as one statement: balance and ageing from the customer ledger, every bill (retail bills are
/// the customer's by phone, as retail credit accounts are), every receipt, everything credited back, and notes.
/// </summary>
public sealed class CustomerAccountService(PharmaBillDbContext context)
{
	public const string RetailBill = "Retail bill";
	public const string WholesaleBill = "Wholesale invoice";

	public static string KindOf(string entryType) => entryType switch
	{
		LedgerEntryTypes.RetailSale => "Bill",
		LedgerEntryTypes.WholesaleInvoice => "Invoice",
		LedgerEntryTypes.Receipt or LedgerEntryTypes.OnAccountReceipt => "Payment",
		LedgerEntryTypes.RetailSaleReturn => "Return",
		LedgerEntryTypes.WholesaleCreditNote => "Credit note",
		LedgerEntryTypes.WholesaleDebitNote => "Debit note",
		LedgerEntryTypes.WholesaleInvoiceCancellation => "Cancellation",
		_ => entryType,
	};

	private static bool IsReceipt(string entryType) => entryType is LedgerEntryTypes.Receipt or LedgerEntryTypes.OnAccountReceipt;

	public async Task<CustomerAccount> GetAccountAsync(Guid customerId, DateOnly asOf, CancellationToken cancellationToken = default)
	{
		Customer customer = await context.Customers.AsNoTracking().SingleOrDefaultAsync(item => item.Id == customerId, cancellationToken)
			?? throw new InvalidOperationException("Customer not found.");
		List<CustomerLedgerEntry> entries = await context.CustomerLedgerEntries.AsNoTracking()
			.Where(entry => entry.CustomerId == customerId)
			.OrderBy(entry => entry.EntryAtUtc).ThenBy(entry => entry.CreatedAtUtc)
			.ToListAsync(cancellationToken);

		var ledger = new List<AccountLedgerLine>(entries.Count + 1);
		decimal balance = customer.OpeningBalance;
		if (customer.OpeningBalance != 0m)
		{
			ledger.Add(new AccountLedgerLine(null, "Opening balance", null, Math.Max(0m, customer.OpeningBalance), Math.Max(0m, -customer.OpeningBalance), balance, null));
		}
		foreach (CustomerLedgerEntry entry in entries)
		{
			balance += entry.Debit - entry.Credit;
			ledger.Add(new AccountLedgerLine(entry.EntryAtUtc, KindOf(entry.EntryType), entry.ReferenceNo, entry.Debit, entry.Credit, balance, entry.Notes));
		}

		var summary = new CustomerAccountSummary(customer.Id, customer.Name, customer.Phone, customer.CreditLimit, customer.CreditDays,
			Round(balance), Age(customer, entries, asOf));

		// Retail bills are this customer's when billed to a patient with the same phone (as retail credit accounts are
		// opened), or when the ledger names them.
		HashSet<Guid> saleIds = entries.Where(entry => entry.EntryType == LedgerEntryTypes.RetailSale && entry.ReferenceId != null)
			.Select(entry => entry.ReferenceId!.Value).ToHashSet();
		string? phone = string.IsNullOrWhiteSpace(customer.Phone) ? null : customer.Phone.Trim();
		List<Sale> sales = await (from sale in context.Sales.AsNoTracking()
				join patient in context.Patients.AsNoTracking() on sale.PatientId equals patient.Id into patients
				from patient in patients.DefaultIfEmpty()
				where saleIds.Contains(sale.Id) || (phone != null && patient != null && patient.Phone == phone)
				select sale)
			.ToListAsync(cancellationToken);
		List<WholesaleInvoice> invoices = await context.WholesaleInvoices.AsNoTracking()
			.Where(invoice => invoice.CustomerId == customerId && invoice.Status != "Draft")
			.ToListAsync(cancellationToken);
		List<AccountBill> bills = sales
			.Select(sale => new AccountBill(RetailBill, sale.Id, sale.InvoiceNo, sale.SaleAtUtc, sale.TotalAmount, sale.PaidAmount,
				sale.PaymentStatus ?? (sale.PaidAmount >= sale.TotalAmount ? "Paid" : "Credit")))
			.Concat(invoices.Select(invoice => new AccountBill(WholesaleBill, invoice.Id, invoice.InvoiceNo, invoice.InvoiceAtUtc, invoice.TotalAmount, invoice.PaidAmount, invoice.Status)))
			.OrderByDescending(bill => bill.AtUtc)
			.ToList();

		// Receipts on the account, and what was paid at the counter on this customer's retail bills.
		string[] counterNotes = sales.Select(sale => $"SaleId={sale.Id:D}; InvoiceNo={sale.InvoiceNo}").ToArray();
		List<Receipt> receipts = await context.Receipts.AsNoTracking()
			.Where(receipt => receipt.CustomerId == customerId || (receipt.Notes != null && counterNotes.Contains(receipt.Notes)))
			.ToListAsync(cancellationToken);
		List<AccountPayment> payments = receipts
			.Select(receipt => new AccountPayment(receipt.ReceiptAtUtc, receipt.ReceiptNo, receipt.Amount, receipt.PaymentMethod,
				Reference(receipt), receipt.CustomerId == customerId ? receipt.Notes : "Paid at the counter on bill " + receipt.Notes!.Split("InvoiceNo=").Last()))
			.OrderByDescending(payment => payment.AtUtc)
			.ToList();

		// Credited back: returns, credit notes and cancellations on the ledger, and returns on the customer's retail bills.
		List<AccountCredit> credits = entries
			.Where(entry => entry.Credit > 0m && !IsReceipt(entry.EntryType))
			.Select(entry => new AccountCredit(entry.EntryAtUtc, KindOf(entry.EntryType), entry.ReferenceNo ?? string.Empty, entry.Credit, entry.Notes))
			.ToList();
		Guid[] linkedSales = sales.Select(sale => sale.Id).ToArray();
		HashSet<string> onLedger = credits.Select(credit => credit.Reference).ToHashSet(StringComparer.OrdinalIgnoreCase);
		Dictionary<Guid, string> saleNumbers = sales.ToDictionary(sale => sale.Id, sale => sale.InvoiceNo);
		List<ReturnNote> returns = await context.ReturnNotes.AsNoTracking()
			.Where(note => note.SourceType == "Sale" && linkedSales.Contains(note.SourceId))
			.ToListAsync(cancellationToken);
		credits.AddRange(returns
			.Where(note => !onLedger.Contains(note.ReturnNo))
			.Select(note => new AccountCredit(note.ReturnAtUtc, "Return", note.ReturnNo, note.TotalAmount, "Return against " + saleNumbers[note.SourceId] + (string.IsNullOrWhiteSpace(note.Reason) ? string.Empty : ": " + note.Reason))));
		credits = credits.OrderByDescending(credit => credit.AtUtc).ToList();

		List<AccountNote> notes = (await context.CustomerNotes.AsNoTracking()
				.Where(note => note.CustomerId == customerId && !note.IsDeleted)
				.ToListAsync(cancellationToken))
			.OrderByDescending(note => note.CreatedAtUtc)
			.Select(note => new AccountNote(note.Id, note.CreatedAtUtc, note.AuthorName, note.Text))
			.ToList();

		return new CustomerAccount(summary, ledger, bills, payments, credits, notes);
	}

	/// <summary>Every active customer who owes something, with the ageing of what they owe, largest first (from the ledger).</summary>
	public async Task<IReadOnlyList<CustomerAgeing>> GetDuesAsync(DateOnly asOf, CancellationToken cancellationToken = default)
	{
		List<Customer> customers = await context.Customers.AsNoTracking().Where(customer => customer.IsActive).ToListAsync(cancellationToken);
		Dictionary<Guid, List<CustomerLedgerEntry>> byCustomer = (await context.CustomerLedgerEntries.AsNoTracking().ToListAsync(cancellationToken))
			.GroupBy(entry => entry.CustomerId)
			.ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.EntryAtUtc).ThenBy(entry => entry.CreatedAtUtc).ToList());
		var dues = new List<CustomerAgeing>();
		foreach (Customer customer in customers)
		{
			AgeingBuckets ageing = Age(customer, byCustomer.GetValueOrDefault(customer.Id) ?? [], asOf);
			if (ageing.Total > 0m)
			{
				dues.Add(new CustomerAgeing(customer.Id, customer.Name, customer.Phone, ageing.Days0To30, ageing.Days31To60, ageing.Days61To90, ageing.Over90, ageing.Total));
			}
		}
		return dues.OrderByDescending(item => item.Total).ToList();
	}

	/// <summary>
	/// What is still owed, by age. Charges (the opening balance, bills, invoices, debit notes) are settled oldest first by
	/// everything credited (payments, returns, credit notes); what remains is aged from its due date (charge date plus
	/// the customer's credit days).
	/// </summary>
	internal static AgeingBuckets Age(Customer customer, IReadOnlyList<CustomerLedgerEntry> entries, DateOnly asOf)
	{
		var charges = new List<(DateOnly Date, decimal Amount)>();
		if (customer.OpeningBalance > 0m)
		{
			charges.Add((customer.CreatedAtUtc.ToBusinessDate(), customer.OpeningBalance));
		}
		charges.AddRange(entries.Where(entry => entry.Debit > 0m && entry.EntryAtUtc.ToBusinessDate() <= asOf).Select(entry => (entry.EntryAtUtc.ToBusinessDate(), entry.Debit)));
		decimal credit = entries.Where(entry => entry.EntryAtUtc.ToBusinessDate() <= asOf).Sum(entry => entry.Credit) + Math.Max(0m, -customer.OpeningBalance);

		decimal[] buckets = new decimal[4];
		foreach ((DateOnly date, decimal amount) in charges.OrderBy(charge => charge.Date))
		{
			decimal settled = Math.Min(amount, credit);
			credit -= settled;
			decimal owed = amount - settled;
			if (owed <= 0m)
			{
				continue;
			}
			int overdue = Math.Max(0, asOf.DayNumber - date.AddDays(customer.CreditDays).DayNumber);
			buckets[overdue switch { <= 30 => 0, <= 60 => 1, <= 90 => 2, _ => 3 }] += owed;
		}
		return new AgeingBuckets(Round(buckets[0]), Round(buckets[1]), Round(buckets[2]), Round(buckets[3]));
	}

	/// <summary>Records the customer's phone number (audited). Another active customer with the same number is refused: retail accounts are matched by phone.</summary>
	public async Task<string> SetPhoneAsync(Guid customerId, string phone, Guid? userId, CancellationToken cancellationToken = default)
	{
		string digits = new(phone.Where(char.IsDigit).ToArray());
		if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
		{
			digits = digits[2..];
		}
		if (digits.Length != 10)
		{
			throw new InvalidOperationException("Enter a 10-digit mobile number.");
		}
		Customer customer = await context.Customers.SingleOrDefaultAsync(item => item.Id == customerId, cancellationToken)
			?? throw new InvalidOperationException("Customer not found.");
		Customer? other = await context.Customers.AsNoTracking()
			.FirstOrDefaultAsync(item => item.Id != customerId && item.IsActive && item.Phone == digits, cancellationToken);
		if (other != null)
		{
			throw new InvalidOperationException(digits + " is already the phone number of " + other.Name + ".");
		}
		string before = string.IsNullOrWhiteSpace(customer.Phone) ? "(none)" : customer.Phone;
		customer.Phone = digits;
		customer.UpdatedAtUtc = BusinessClock.UtcNow;
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			Action = "CustomerPhoneSet",
			EntityName = "Customer",
			EntityId = customer.Id,
			Details = $"{customer.Name}: phone {before} -> {digits}"
		});
		await context.SaveChangesAsync(cancellationToken);
		return digits;
	}

	public async Task<AccountNote> AddNoteAsync(Guid customerId, string text, AppUser author, CancellationToken cancellationToken = default)
	{
		string trimmed = (text ?? string.Empty).Trim();
		if (trimmed.Length == 0)
		{
			throw new InvalidOperationException("Write the note first.");
		}
		if (trimmed.Length > 2000)
		{
			throw new InvalidOperationException("Keep a note under 2000 characters.");
		}
		if (!await context.Customers.AnyAsync(item => item.Id == customerId, cancellationToken))
		{
			throw new InvalidOperationException("Customer not found.");
		}
		var note = new CustomerNote
		{
			CustomerId = customerId,
			Text = trimmed,
			AuthorUserId = author.Id,
			AuthorName = string.IsNullOrWhiteSpace(author.DisplayName) ? author.UserName : author.DisplayName
		};
		context.CustomerNotes.Add(note);
		await context.SaveChangesAsync(cancellationToken);
		return new AccountNote(note.Id, note.CreatedAtUtc, note.AuthorName, note.Text);
	}

	private static string? Reference(Receipt receipt)
	{
		var parts = new List<string>();
		if (!string.IsNullOrWhiteSpace(receipt.ReferenceNumber))
		{
			parts.Add(receipt.ReferenceNumber.Trim());
		}
		if (!string.IsNullOrWhiteSpace(receipt.ChequeNumber))
		{
			parts.Add("Cheque " + receipt.ChequeNumber.Trim() + (string.IsNullOrWhiteSpace(receipt.BankName) ? string.Empty : ", " + receipt.BankName.Trim()));
		}
		return parts.Count == 0 ? null : string.Join(" · ", parts);
	}

	private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
