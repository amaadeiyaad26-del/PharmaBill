using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record WholesaleCreditLineInput(Guid InvoiceItemId, decimal Quantity, bool Restock);

public sealed record ExpiryReturnTrackerRow(
    Guid BatchId,
    string DrugName,
    string BatchNo,
    DateOnly? ExpiryDate,
    decimal OnHand,
    decimal PurchaseRate,
    string Supplier);

public sealed class WholesaleReturnsService(
    IUnitOfWork unitOfWork,
    IEntitlementService entitlementService)
{
    public async Task<ReturnNote> CreateCreditNoteAsync(
        Guid invoiceId,
        string returnNo,
        DateOnly returnDate,
        IReadOnlyList<WholesaleCreditLineInput> lines,
        string reason,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        await DemandReturnAllowed(role, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(returnNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (returnDate > DateOnly.FromDateTime(DateTime.Today) ||
            lines.Count == 0 ||
            lines.Any(line => line.Quantity <= 0))
        {
            throw new InvalidOperationException("Credit note needs a non-future date, reason, and positive item quantities.");
        }

        var context = unitOfWork.Context;
        var invoice = await context.WholesaleInvoices.SingleOrDefaultAsync(
            item => item.Id == invoiceId && item.Status == "Posted",
            cancellationToken) ?? throw new InvalidOperationException("Select a posted wholesale invoice.");
        if (await context.ReturnNotes.IgnoreQueryFilters().AnyAsync(item => item.ReturnNo == returnNo.Trim(), cancellationToken))
        {
            throw new InvalidOperationException("That return number has already been used.");
        }

        var grouped = lines.GroupBy(line => new { line.InvoiceItemId, line.Restock })
            .Select(group => new WholesaleCreditLineInput(
                group.Key.InvoiceItemId,
                group.Sum(line => line.Quantity),
                group.Key.Restock)).ToArray();
        var invoiceLines = await context.WholesaleInvoiceItems
            .Where(item => item.WholesaleInvoiceId == invoice.Id)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var returnedItems = await context.WholesaleReturnItems
            .Where(item => grouped.Select(line => line.InvoiceItemId).Contains(item.WholesaleInvoiceItemId))
            .ToListAsync(cancellationToken);
        foreach (var line in grouped)
        {
            if (!invoiceLines.TryGetValue(line.InvoiceItemId, out var invoiceLine))
            {
                throw new InvalidOperationException("A credit-note item does not belong to the selected invoice.");
            }

            var previouslyReturned = returnedItems
                .Where(item => item.WholesaleInvoiceItemId == line.InvoiceItemId)
                .Sum(item => item.Quantity);
            if (line.Quantity + previouslyReturned > invoiceLine.Quantity)
            {
                throw new InvalidOperationException("Credit-note quantity exceeds the sold paid quantity for its original batch.");
            }
        }

        var creditTotal = grouped.Sum(line =>
        {
            var invoiceLine = invoiceLines[line.InvoiceItemId];
            var grossUnitRate = invoiceLine.Quantity == 0 ? 0m : invoiceLine.LineTotal / invoiceLine.Quantity;
            return RoundMoney(grossUnitRate * line.Quantity);
        });
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var note = new ReturnNote
        {
            ReturnNo = returnNo.Trim(),
            SourceType = "WholesaleCreditNote",
            SourceId = invoice.Id,
            ReturnAtUtc = DateTime.UtcNow,
            TotalAmount = creditTotal,
            Reason = reason.Trim(),
            Notes = $"Against invoice {invoice.InvoiceNo}; quarantined returns remain outside saleable stock."
        };
        context.ReturnNotes.Add(note);
        foreach (var line in grouped)
        {
            var invoiceLine = invoiceLines[line.InvoiceItemId];
            var batch = await context.Batches.SingleAsync(item => item.Id == invoiceLine.BatchId, cancellationToken);
            var invoiceUnitGross = invoiceLine.LineTotal / invoiceLine.Quantity;
            var creditAmount = RoundMoney(invoiceUnitGross * line.Quantity);
            var returned = new WholesaleReturnItem
            {
                ReturnNoteId = note.Id,
                WholesaleInvoiceItemId = invoiceLine.Id,
                BatchId = invoiceLine.BatchId,
                DrugId = invoiceLine.DrugId,
                Quantity = line.Quantity,
                CreditAmount = creditAmount,
                Restocked = line.Restock,
                Quarantined = !line.Restock
            };
            context.WholesaleReturnItems.Add(returned);
            if (line.Restock)
            {
                batch.Quantity += line.Quantity;
                context.StockMovements.Add(new StockMovement
                {
                    BatchId = batch.Id,
                    DrugId = invoiceLine.DrugId,
                    QuantityChange = line.Quantity,
                    MovementType = "WholesaleCreditReturnRestocked",
                    ReferenceType = nameof(ReturnNote),
                    ReferenceId = note.Id,
                    Notes = reason.Trim()
                });
            }
        }

        context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
        {
            CustomerId = invoice.CustomerId,
            EntryAtUtc = note.ReturnAtUtc,
            EntryType = "WholesaleCreditNote",
            ReferenceId = invoice.Id,
            ReferenceNo = note.ReturnNo,
            Credit = creditTotal,
            Notes = $"Credit note {note.ReturnNo} against {invoice.InvoiceNo}"
        });
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = note.ReturnAtUtc,
            Action = "WholesaleCreditNoteCreated",
            EntityName = nameof(ReturnNote),
            EntityId = note.Id,
            Details = $"{note.ReturnNo}; invoice {invoice.InvoiceNo}; amount {creditTotal}; {grouped.Length} lines."
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return note;
    }

    public async Task<ReturnNote> CreateDebitNoteAsync(
        Guid customerId,
        string debitNoteNo,
        DateOnly date,
        decimal amount,
        string reason,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        await DemandReturnAllowed(role, cancellationToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(debitNoteNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (amount <= 0 || date > DateOnly.FromDateTime(DateTime.Today))
        {
            throw new InvalidOperationException("Debit note requires a positive amount and a non-future date.");
        }

        var context = unitOfWork.Context;
        var customer = await context.Customers.SingleOrDefaultAsync(item => item.Id == customerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");
        if (await context.ReturnNotes.IgnoreQueryFilters().AnyAsync(item => item.ReturnNo == debitNoteNo.Trim(), cancellationToken))
        {
            throw new InvalidOperationException("That debit note number has already been used.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var note = new ReturnNote
        {
            ReturnNo = debitNoteNo.Trim(),
            SourceType = "WholesaleDebitNote",
            SourceId = customer.Id,
            ReturnAtUtc = DateTime.UtcNow,
            TotalAmount = RoundMoney(amount),
            Reason = reason.Trim()
        };
        context.ReturnNotes.Add(note);
        context.CustomerLedgerEntries.Add(new CustomerLedgerEntry
        {
            CustomerId = customer.Id,
            EntryAtUtc = note.ReturnAtUtc,
            EntryType = "WholesaleDebitNote",
            ReferenceId = note.Id,
            ReferenceNo = note.ReturnNo,
            Debit = note.TotalAmount,
            Notes = note.Reason
        });
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = note.ReturnAtUtc,
            Action = "WholesaleDebitNoteCreated",
            EntityName = nameof(ReturnNote),
            EntityId = note.Id,
            Details = $"{note.ReturnNo}; customer {customer.Name}; amount {note.TotalAmount}."
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return note;
    }

    public async Task<IReadOnlyList<ExpiryReturnTrackerRow>> GetExpiryReturnTrackerAsync(
        DateOnly through,
        CancellationToken cancellationToken = default)
    {
        var batches = await unitOfWork.Context.Batches.AsNoTracking()
            .Where(batch => batch.ExpiryDate.HasValue && batch.ExpiryDate.Value <= through)
            .ToListAsync(cancellationToken);
        var drugIds = batches.Select(batch => batch.DrugId).Distinct().ToArray();
        var supplierIds = batches.Where(batch => batch.SupplierId.HasValue)
            .Select(batch => batch.SupplierId!.Value).Distinct().ToArray();
        var drugs = await unitOfWork.Context.Drugs.AsNoTracking()
            .Where(drug => drugIds.Contains(drug.Id))
            .ToDictionaryAsync(drug => drug.Id, cancellationToken);
        var suppliers = await unitOfWork.Context.Suppliers.AsNoTracking()
            .Where(supplier => supplierIds.Contains(supplier.Id))
            .ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, cancellationToken);
        var batchIds = batches.Select(batch => batch.Id).ToArray();
        var stock = await unitOfWork.Context.StockMovements.AsNoTracking()
            .Where(movement => batchIds.Contains(movement.BatchId))
            .GroupBy(movement => movement.BatchId)
            .Select(group => new { BatchId = group.Key, Quantity = group.Sum(movement => movement.QuantityChange) })
            .ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
        return batches.Select(batch => new ExpiryReturnTrackerRow(
                batch.Id,
                drugs[batch.DrugId].Name,
                batch.BatchNo,
                batch.ExpiryDate,
                stock.GetValueOrDefault(batch.Id),
                batch.PurchasePrice,
                batch.SupplierId.HasValue ? suppliers.GetValueOrDefault(batch.SupplierId.Value, string.Empty) : string.Empty))
            .Where(row => row.OnHand > 0)
            .OrderBy(row => row.ExpiryDate)
            .ToArray();
    }

    private async Task DemandReturnAllowed(UserRole role, CancellationToken cancellationToken)
    {
        if (!PermissionMatrix.Allows(role, AppPermission.ProcessReturn))
        {
            throw new UnauthorizedAccessException("Your role cannot process wholesale returns.");
        }

        if (!await entitlementService.CanPerformAsync(ProtectedOperation.ProcessReturn, cancellationToken))
        {
            throw new InvalidOperationException("Returns are unavailable in read-only mode.");
        }
    }

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
