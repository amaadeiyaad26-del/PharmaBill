using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record LedgerRow(
    DateTime EntryAtUtc,
    string EntryType,
    string? ReferenceNo,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    string? Notes);

public sealed record CustomerAgeing(
    Guid CustomerId,
    string CustomerName,
    string? Phone,
    decimal Current0To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal Over90,
    decimal Total);

public sealed record CashBankBookRow(
    DateTime AtUtc,
    string ReceiptNo,
    string Party,
    string Direction,
    string Method,
    string? Reference,
    decimal Amount);

public sealed class WholesaleAccountsService(IUnitOfWork unitOfWork)
{
    public async Task<Receipt> RecordReceiptAsync(
        Guid customerId,
        Guid? wholesaleInvoiceId,
        decimal amount,
        string paymentMethod,
        string? referenceNumber,
        string? chequeNumber,
        DateOnly? chequeDate,
        string? bankName,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(paymentMethod))
        {
            throw new InvalidOperationException("Receipt amount must be positive and payment method is required.");
        }

        var context = unitOfWork.Context;
        var customer = await context.Customers.SingleOrDefaultAsync(
            item => item.Id == customerId && item.IsActive,
            cancellationToken) ?? throw new InvalidOperationException("Select an active customer.");
        WholesaleInvoice? invoice = null;
        if (wholesaleInvoiceId.HasValue)
        {
            invoice = await context.WholesaleInvoices.SingleOrDefaultAsync(
                item => item.Id == wholesaleInvoiceId.Value &&
                        item.CustomerId == customerId && item.Status == "Posted",
                cancellationToken) ?? throw new InvalidOperationException("Select an unpaid invoice for this customer.");
            var receiptsReceived = await context.Receipts
                .Where(receipt => receipt.WholesaleInvoiceId == invoice.Id)
                .SumAsync(receipt => (decimal?)receipt.Amount, cancellationToken) ?? 0m;
            var alreadyReceived = Math.Max(invoice.PaidAmount, receiptsReceived);
            if (alreadyReceived + amount > invoice.TotalAmount)
            {
                throw new InvalidOperationException("Receipt exceeds the remaining invoice balance.");
            }
        }

        var method = paymentMethod.Trim();
        if (method.Equals("Cheque", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(chequeNumber) || !chequeDate.HasValue || string.IsNullOrWhiteSpace(bankName)))
        {
            throw new InvalidOperationException("Cheque receipts need cheque number, cheque date and bank name.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var receipt = new Receipt
        {
            CustomerId = customer.Id,
            WholesaleInvoiceId = invoice?.Id,
            ReceiptNo = $"RCPT-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30],
            ReceiptAtUtc = DateTime.UtcNow,
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            PaymentMethod = method,
            ReferenceNumber = NullIfWhiteSpace(referenceNumber),
            ChequeNumber = NullIfWhiteSpace(chequeNumber),
            ChequeDate = chequeDate,
            BankName = NullIfWhiteSpace(bankName),
            Notes = invoice is null ? "On-account receipt" : $"Against invoice {invoice.InvoiceNo}"
        };
        context.Receipts.Add(receipt);
        context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
        {
            CustomerId = customer.Id,
            EntryAtUtc = receipt.ReceiptAtUtc,
            EntryType = invoice is null ? "OnAccountReceipt" : "Receipt",
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
            EntityName = nameof(Receipt),
            EntityId = receipt.Id,
            Details = $"Customer {customer.Name}; amount {receipt.Amount}; method {method}."
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return receipt;
    }

    public async Task<Receipt> RecordSupplierPaymentAsync(
        Guid supplierId,
        decimal amount,
        string paymentMethod,
        string? referenceNumber,
        string? chequeNumber,
        DateOnly? chequeDate,
        string? bankName,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(paymentMethod))
        {
            throw new InvalidOperationException("Payment amount must be positive and method is required.");
        }

        var context = unitOfWork.Context;
        var supplier = await context.Suppliers.SingleOrDefaultAsync(
            item => item.Id == supplierId && item.IsActive,
            cancellationToken) ?? throw new InvalidOperationException("Select an active supplier.");
        var method = paymentMethod.Trim();
        if (method.Equals("Cheque", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(chequeNumber) || !chequeDate.HasValue || string.IsNullOrWhiteSpace(bankName)))
        {
            throw new InvalidOperationException("Cheque payments need cheque number, cheque date and bank name.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var payment = new Receipt
        {
            SupplierId = supplier.Id,
            ReceiptNo = $"PAY-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..30],
            ReceiptAtUtc = now,
            Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero),
            PaymentMethod = method,
            ReferenceNumber = NullIfWhiteSpace(referenceNumber),
            ChequeNumber = NullIfWhiteSpace(chequeNumber),
            ChequeDate = chequeDate,
            BankName = NullIfWhiteSpace(bankName),
            Notes = $"Supplier payment to {supplier.Name}"
        };
        context.Receipts.Add(payment);
        context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
        {
            SupplierId = supplier.Id,
            EntryAtUtc = now,
            EntryType = "SupplierPayment",
            ReferenceId = payment.Id,
            ReferenceNo = payment.ReceiptNo,
            Debit = payment.Amount,
            Notes = payment.Notes
        });
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = now,
            Action = "WholesaleSupplierPaymentRecorded",
            EntityName = nameof(Receipt),
            EntityId = payment.Id,
            Details = $"Supplier {supplier.Name}; amount {payment.Amount}; method {method}."
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return payment;
    }

    public async Task<IReadOnlyList<LedgerRow>> GetCustomerLedgerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await unitOfWork.Context.Customers.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");
        var entries = await unitOfWork.Context.CustomerLedgerEntries.AsNoTracking()
            .Where(entry => entry.CustomerId == customerId)
            .OrderBy(entry => entry.EntryAtUtc)
            .ThenBy(entry => entry.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var balance = customer.OpeningBalance;
        var result = new List<LedgerRow>(entries.Count + 1);
        if (customer.OpeningBalance != 0m)
        {
            result.Add(new LedgerRow(DateTime.MinValue, "Opening balance", null,
                customer.OpeningBalance, 0m, balance, null));
        }

        foreach (var entry in entries)
        {
            balance += entry.Debit - entry.Credit;
            result.Add(new LedgerRow(
                entry.EntryAtUtc,
                entry.EntryType,
                entry.ReferenceNo,
                entry.Debit,
                entry.Credit,
                balance,
                entry.Notes));
        }

        return result;
    }

    public async Task<IReadOnlyList<CustomerAgeing>> GetAgeingAsync(
        DateOnly asOfDate,
        CancellationToken cancellationToken = default)
    {
        var customers = await unitOfWork.Context.Customers.AsNoTracking()
            .Where(customer => customer.IsActive)
            .ToListAsync(cancellationToken);
        var invoices = await unitOfWork.Context.WholesaleInvoices.AsNoTracking()
            .Where(invoice => invoice.Status == "Posted")
            .ToListAsync(cancellationToken);
        var receipts = await unitOfWork.Context.Receipts.AsNoTracking()
            .Where(receipt => receipt.CustomerId.HasValue)
            .ToListAsync(cancellationToken);
        var creditEntries = await unitOfWork.Context.CustomerLedgerEntries.AsNoTracking()
            .Where(entry => entry.EntryType.Contains("CreditNote") ||
                            entry.EntryType.Contains("Cancellation"))
            .ToListAsync(cancellationToken);
        var byCustomer = invoices.GroupBy(invoice => invoice.CustomerId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var result = new List<CustomerAgeing>();
        foreach (var customer in customers)
        {
            var values = new decimal[4];
            if (byCustomer.TryGetValue(customer.Id, out var customerInvoices))
            {
                var onAccountAvailable = receipts.Where(receipt =>
                        receipt.CustomerId == customer.Id &&
                        !receipt.WholesaleInvoiceId.HasValue &&
                        DateOnly.FromDateTime(receipt.ReceiptAtUtc.ToLocalTime()) <= asOfDate)
                    .Sum(receipt => receipt.Amount);
                foreach (var invoice in customerInvoices.OrderBy(item => item.InvoiceAtUtc))
                {
                    var receiptsAgainstInvoice = receipts.Where(receipt => receipt.WholesaleInvoiceId == invoice.Id &&
                            DateOnly.FromDateTime(receipt.ReceiptAtUtc.ToLocalTime()) <= asOfDate)
                        .Sum(receipt => receipt.Amount);
                    var paid = Math.Max(invoice.PaidAmount, receiptsAgainstInvoice);
                    var credits = creditEntries.Where(entry => entry.CustomerId == customer.Id &&
                                    (entry.ReferenceId == invoice.Id ||
                                     (entry.EntryType.Contains("CreditNote") &&
                                      entry.Notes?.Contains(invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase) == true)) &&
                            DateOnly.FromDateTime(entry.EntryAtUtc.ToLocalTime()) <= asOfDate)
                        .Sum(entry => entry.Credit);
                    var outstanding = Math.Max(0m, invoice.TotalAmount - paid - credits);
                    var appliedOnAccount = Math.Min(outstanding, onAccountAvailable);
                    outstanding -= appliedOnAccount;
                    onAccountAvailable -= appliedOnAccount;
                    if (outstanding == 0)
                    {
                        continue;
                    }

                    var dueDate = DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime())
                        .AddDays(customer.CreditDays);
                    var ageDays = Math.Max(0, asOfDate.DayNumber - dueDate.DayNumber);
                    values[ageDays switch
                    {
                        <= 30 => 0,
                        <= 60 => 1,
                        <= 90 => 2,
                        _ => 3
                    }] += outstanding;
                }
            }

            var total = values.Sum();
            if (total > 0)
            {
                result.Add(new CustomerAgeing(
                    customer.Id, customer.Name, customer.Phone, values[0], values[1], values[2], values[3], total));
            }
        }

        return result.OrderByDescending(item => item.Total).ToArray();
    }

    public async Task<IReadOnlyList<CashBankBookRow>> GetCashBankBookAsync(
        DateTime fromUtc,
        DateTime toUtcExclusive,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc.Kind != DateTimeKind.Utc || toUtcExclusive.Kind != DateTimeKind.Utc ||
            toUtcExclusive <= fromUtc)
        {
            throw new ArgumentException("Cash/bank book period must be a non-empty UTC range.");
        }

        var receipts = await unitOfWork.Context.Receipts.AsNoTracking()
            .Where(item => item.ReceiptAtUtc >= fromUtc && item.ReceiptAtUtc < toUtcExclusive)
            .ToListAsync(cancellationToken);
        var customers = await unitOfWork.Context.Customers.AsNoTracking()
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var suppliers = await unitOfWork.Context.Suppliers.AsNoTracking()
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        return receipts.Select(receipt => new CashBankBookRow(
                receipt.ReceiptAtUtc,
                receipt.ReceiptNo,
                receipt.CustomerId.HasValue
                    ? customers.GetValueOrDefault(receipt.CustomerId.Value, "Unknown customer")
                    : receipt.SupplierId.HasValue
                        ? suppliers.GetValueOrDefault(receipt.SupplierId.Value, "Unknown supplier")
                        : "Unassigned",
                receipt.CustomerId.HasValue ? "In" : "Out",
                receipt.PaymentMethod,
                receipt.ChequeNumber ?? receipt.ReferenceNumber,
                receipt.Amount))
            .OrderBy(item => item.AtUtc)
            .ToArray();
    }

    public async Task<IReadOnlyList<LedgerRow>> GetSupplierLedgerAsync(
        Guid supplierId,
        CancellationToken cancellationToken = default)
    {
        if (!await unitOfWork.Context.Suppliers.AnyAsync(item => item.Id == supplierId, cancellationToken))
        {
            throw new InvalidOperationException("Supplier not found.");
        }

        var entries = await unitOfWork.Context.SupplierLedgerEntries.AsNoTracking()
            .Where(entry => entry.SupplierId == supplierId)
            .OrderBy(entry => entry.EntryAtUtc)
            .ThenBy(entry => entry.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var balance = 0m;
        return entries.Select(entry =>
        {
            balance += entry.Credit - entry.Debit;
            return new LedgerRow(entry.EntryAtUtc, entry.EntryType, entry.ReferenceNo,
                entry.Debit, entry.Credit, balance, entry.Notes);
        }).ToArray();
    }

    public async Task<IReadOnlyList<(Guid SupplierId, string SupplierName, decimal Payable)>> GetSupplierPayablesAsync(
        CancellationToken cancellationToken = default)
    {
        var suppliers = await unitOfWork.Context.Suppliers.AsNoTracking()
            .Where(item => item.IsActive)
            .ToListAsync(cancellationToken);
        var entries = await unitOfWork.Context.SupplierLedgerEntries.AsNoTracking()
            .GroupBy(item => item.SupplierId)
            .Select(group => new { SupplierId = group.Key, Credit = group.Sum(item => item.Credit), Debit = group.Sum(item => item.Debit) })
            .ToDictionaryAsync(item => item.SupplierId, cancellationToken);
        return suppliers.Select(supplier =>
                (supplier.Id, supplier.Name, Math.Max(0m, entries.GetValueOrDefault(supplier.Id)?.Credit -
                    entries.GetValueOrDefault(supplier.Id)?.Debit ?? 0m)))
            .Where(item => item.Item3 > 0)
            .OrderByDescending(item => item.Item3)
            .ToArray();
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
