using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record PurchaseLineInput(
    Guid DrugId,
    string BatchNo,
    DateOnly? ExpiryDate,
    decimal Quantity,
    decimal FreeQuantity,
    decimal Mrp,
    decimal Rate,
    decimal GstRate,
    decimal DiscountAmount,
    decimal Amount,
    string? Rack);

public sealed record SavePurchaseInput(
    Guid SupplierId,
    string InvoiceNo,
    DateOnly InvoiceDate,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal GrandTotal,
    IReadOnlyList<PurchaseLineInput> Items,
    string? Notes = null);

public sealed record PurchaseReturnLineInput(Guid BatchId, decimal Quantity);

public sealed class PurchaseService(
    IUnitOfWork unitOfWork,
    IEntitlementService entitlementService)
{
    public async Task<bool> IsDuplicateInvoiceAsync(
        Guid supplierId,
        string invoiceNo,
        CancellationToken cancellationToken = default) =>
        await unitOfWork.Context.PurchaseInvoices.IgnoreQueryFilters().AnyAsync(
            invoice => invoice.SupplierId == supplierId &&
                       invoice.InvoiceNo == invoiceNo.Trim(),
            cancellationToken);

    public async Task<PurchaseInvoice> SavePurchaseAsync(
        SavePurchaseInput input,
        Guid actingUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        ValidateHeader(input);
        if (!PermissionMatrix.Allows(role, AppPermission.ManagePurchases))
        {
            throw new UnauthorizedAccessException("Your role cannot enter purchases.");
        }

        if (!await entitlementService.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken))
        {
            throw new InvalidOperationException("Purchase entry is unavailable in read-only mode.");
        }

        if (await IsDuplicateInvoiceAsync(input.SupplierId, input.InvoiceNo, cancellationToken))
        {
            throw new InvalidOperationException("A purchase invoice with this supplier and invoice number already exists.");
        }

        var supplierExists = await unitOfWork.Context.Suppliers.AnyAsync(
            supplier => supplier.Id == input.SupplierId && supplier.IsActive,
            cancellationToken);
        if (!supplierExists)
        {
            throw new InvalidOperationException("Select an active supplier.");
        }

        var drugIds = input.Items.Select(item => item.DrugId).Distinct().ToArray();
        var drugs = await unitOfWork.Context.Drugs
            .Where(drug => drugIds.Contains(drug.Id) && drug.IsActive)
            .ToDictionaryAsync(drug => drug.Id, cancellationToken);
        if (drugs.Count != drugIds.Length)
        {
            throw new InvalidOperationException("One or more purchase items refer to an unavailable medicine.");
        }

        var validatedItems = input.Items.Select(item => ValidateLine(item, drugs[item.DrugId])).ToArray();
        var expectedSubtotal = validatedItems.Sum(item => item.BaseAmount);
        var expectedTax = validatedItems.Sum(item => item.TaxAmount);
        var expectedTotal = expectedSubtotal + expectedTax;
        if (RoundMoney(input.Subtotal) != RoundMoney(expectedSubtotal) ||
            RoundMoney(input.TaxAmount) != RoundMoney(expectedTax) ||
            RoundMoney(input.GrandTotal) != RoundMoney(expectedTotal))
        {
            throw new InvalidOperationException("Purchase totals do not equal the validated item totals.");
        }

        // Joins a caller's transaction (e.g. Add stock creating a drug first) so everything stays atomic.
        var ownedTransaction = unitOfWork.Context.Database.CurrentTransaction is null
            ? await unitOfWork.BeginTransactionAsync(cancellationToken)
            : null;
        await using var transaction = ownedTransaction;
        var finalTotal =  RoundMoney(expectedTotal - input.DiscountAmount);
        var invoice = new PurchaseInvoice
        {
            SupplierId = input.SupplierId,
            InvoiceNo = input.InvoiceNo.Trim(),
            InvoiceDate = input.InvoiceDate,
            Subtotal = RoundMoney(expectedSubtotal),
            TaxAmount = RoundMoney(expectedTax),
            DiscountAmount = RoundMoney(input.DiscountAmount),
            TotalAmount = finalTotal,
            Status = "Posted",
            Notes = NullIfWhiteSpace(input.Notes)
        };
        unitOfWork.Context.PurchaseInvoices.Add(invoice);
        unitOfWork.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
        {
            SupplierId = input.SupplierId,
            EntryAtUtc = DateTime.UtcNow,
            EntryType = "PurchaseInvoice",
            ReferenceId = invoice.Id,
            ReferenceNo = invoice.InvoiceNo,
            Credit = finalTotal,
            Notes = $"Purchase invoice {invoice.InvoiceNo.Trim()}"
        });

        var batchesByKey = new Dictionary<(Guid DrugId, string BatchNo, DateOnly? ExpiryDate), Batch>();
        var resultingQuantities = new Dictionary<Guid, decimal>();
        foreach (var line in validatedItems)
        {
            var inputLine = line.Input;
            var batchKey = (inputLine.DrugId, inputLine.BatchNo.Trim(), inputLine.ExpiryDate);
            if (!batchesByKey.TryGetValue(batchKey, out var batch))
            {
                batch = await unitOfWork.Context.Batches.SingleOrDefaultAsync(
                    item => item.DrugId == batchKey.DrugId &&
                            item.SupplierId == input.SupplierId &&
                            item.BatchNo == batchKey.Item2 &&
                            item.ExpiryDate == batchKey.ExpiryDate,
                    cancellationToken);
            }

            if (batch is null)
            {
                batch = new Batch
                {
                    DrugId = inputLine.DrugId,
                    SupplierId = input.SupplierId,
                    BatchNo = inputLine.BatchNo.Trim(),
                    ExpiryDate = inputLine.ExpiryDate,
                    Quantity = 0,
                    Mrp = inputLine.Mrp,
                    Ptr = inputLine.Rate,
                    PurchasePrice = inputLine.Rate,
                    SalePrice = inputLine.Mrp,
                    Rack = NullIfWhiteSpace(inputLine.Rack)
                };
                unitOfWork.Context.Batches.Add(batch);
            }
            else
            {
                batch.Mrp = inputLine.Mrp;
                batch.Ptr = inputLine.Rate;
                batch.PurchasePrice = inputLine.Rate;
                batch.SalePrice = inputLine.Mrp;
                if (!string.IsNullOrWhiteSpace(inputLine.Rack))
                {
                    batch.Rack = inputLine.Rack.Trim();
                }
            }

            batchesByKey[batchKey] = batch;
            if (!resultingQuantities.TryGetValue(batch.Id, out var currentQuantity))
            {
                currentQuantity = await unitOfWork.Context.StockMovements
                    .Where(movement => movement.BatchId == batch.Id)
                    .SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken) ?? 0m;
            }

            var receivedQuantity = inputLine.Quantity + inputLine.FreeQuantity;
            batch.Quantity = currentQuantity + receivedQuantity;
            resultingQuantities[batch.Id] = batch.Quantity;
            unitOfWork.Context.PurchaseItems.Add(new PurchaseItem
            {
                PurchaseInvoiceId = invoice.Id,
                DrugId = inputLine.DrugId,
                BatchId = batch.Id,
                Quantity = inputLine.Quantity,
                FreeQuantity = inputLine.FreeQuantity,
                BatchNo = inputLine.BatchNo.Trim(),
                ExpiryDate = inputLine.ExpiryDate,
                Mrp = inputLine.Mrp,
                Ptr = inputLine.Rate,
                UnitPrice = inputLine.Rate,
                DiscountAmount = inputLine.DiscountAmount,
                TaxRate = inputLine.GstRate,
                LineTotal = line.LineTotal
            });
            unitOfWork.Context.StockMovements.Add(new StockMovement
            {
                BatchId = batch.Id,
                DrugId = inputLine.DrugId,
                QuantityChange = receivedQuantity,
                MovementType = "PurchaseReceipt",
                ReferenceType = nameof(PurchaseInvoice),
                ReferenceId = invoice.Id,
                MovementAtUtc = DateTime.UtcNow,
                Notes = input.InvoiceNo.Trim()
            });

            var drug = drugs[inputLine.DrugId];
            var catalogInfo = await GetCatalogInfoAsync(drug, cancellationToken);
            var registerType = GetControlledRegisterType(drug.Schedule, catalogInfo);
            if (registerType is not null)
            {
                unitOfWork.Context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
                {
                    RegisterType = registerType,
                    DrugId = drug.Id,
                    SupplierId = input.SupplierId,
                    BatchId = batch.Id,
                    BatchNo = batch.BatchNo,
                    Quantity = receivedQuantity,
                    EntryAtUtc = DateTime.UtcNow,
                    Notes = $"Purchase invoice {input.InvoiceNo.Trim()}"
                });
            }
        }

        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = "PurchaseInvoicePosted",
            EntityName = nameof(PurchaseInvoice),
            EntityId = invoice.Id,
            Details = $"Invoice {invoice.InvoiceNo}; total {invoice.TotalAmount}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (ownedTransaction is not null)
        {
            await ownedTransaction.CommitAsync(cancellationToken);
        }

        return invoice;
    }

    public async Task<PurchaseReturn> SavePurchaseReturnAsync(
        Guid supplierId,
        string returnNo,
        DateOnly returnDate,
        IReadOnlyList<PurchaseReturnLineInput> lines,
        string reason,
        Guid actingUserId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionMatrix.Allows(role, AppPermission.ProcessReturn))
        {
            throw new UnauthorizedAccessException("Your role cannot process purchase returns.");
        }

        if (!await entitlementService.CanPerformAsync(ProtectedOperation.ProcessReturn, cancellationToken))
        {
            throw new InvalidOperationException("Purchase returns are unavailable in read-only mode.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(returnNo);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (returnDate > DateOnly.FromDateTime(DateTime.Today))
        {
            throw new InvalidOperationException("Purchase return date cannot be in the future.");
        }

        if (lines.Count == 0 || lines.Any(line => line.Quantity <= 0))
        {
            throw new InvalidOperationException("Add at least one purchase-return line with a positive quantity.");
        }

        lines = lines.GroupBy(line => line.BatchId)
            .Select(group => new PurchaseReturnLineInput(group.Key, group.Sum(line => line.Quantity)))
            .ToArray();
        if (await unitOfWork.Context.PurchaseReturns.IgnoreQueryFilters().AnyAsync(
                item => item.ReturnNo == returnNo.Trim(),
                cancellationToken))
        {
            throw new InvalidOperationException("A purchase return with this number already exists.");
        }

        var supplier = await unitOfWork.Context.Suppliers.SingleOrDefaultAsync(
            item => item.Id == supplierId && item.IsActive,
            cancellationToken) ?? throw new InvalidOperationException("Select an active supplier.");
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var batches = await unitOfWork.Context.Batches
            .Where(batch => lines.Select(line => line.BatchId).Contains(batch.Id) &&
                            batch.SupplierId == supplier.Id)
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);
        if (batches.Count != lines.Select(line => line.BatchId).Distinct().Count())
        {
            throw new InvalidOperationException("Every return batch must belong to the selected supplier.");
        }

        var onHandByBatch = new Dictionary<Guid, decimal>();
        foreach (var line in lines)
        {
            var batch = batches[line.BatchId];
            var onHand = await unitOfWork.Context.StockMovements
                .Where(movement => movement.BatchId == batch.Id)
                .SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken) ?? 0;
            if (line.Quantity > onHand)
            {
                throw new InvalidOperationException($"Return quantity exceeds stock for batch {batch.BatchNo}.");
            }

            onHandByBatch[batch.Id] = onHand;
        }

        var purchaseReturn = new PurchaseReturn
        {
            SupplierId = supplierId,
            ReturnNo = returnNo.Trim(),
            ReturnDate = returnDate,
            Reason = reason.Trim()
        };
        unitOfWork.Context.PurchaseReturns.Add(purchaseReturn);
        foreach (var line in lines)
        {
            var batch = batches[line.BatchId];
            var unitPrice = batch.PurchasePrice;
            var lineTotal = RoundMoney(unitPrice * line.Quantity);
            purchaseReturn.TotalAmount += lineTotal;
            batch.Quantity = onHandByBatch[batch.Id] - line.Quantity;
            onHandByBatch[batch.Id] = batch.Quantity;
            unitOfWork.Context.PurchaseReturnItems.Add(new PurchaseReturnItem
            {
                PurchaseReturnId = purchaseReturn.Id,
                BatchId = batch.Id,
                DrugId = batch.DrugId,
                Quantity = line.Quantity,
                UnitPrice = unitPrice,
                LineTotal = lineTotal
            });
            unitOfWork.Context.StockMovements.Add(new StockMovement
            {
                BatchId = batch.Id,
                DrugId = batch.DrugId,
                QuantityChange = -line.Quantity,
                MovementType = "PurchaseReturn",
                ReferenceType = nameof(PurchaseReturn),
                ReferenceId = purchaseReturn.Id,
                Notes = purchaseReturn.ReturnNo
            });

            var drug = await unitOfWork.Context.Drugs.SingleAsync(item => item.Id == batch.DrugId, cancellationToken);
            var catalogInfo = await GetCatalogInfoAsync(drug, cancellationToken);
            var registerType = GetControlledRegisterType(drug.Schedule, catalogInfo);
            unitOfWork.Context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
            {
                RegisterType = registerType ?? "PurchaseReturn",
                DrugId = drug.Id,
                SupplierId = supplierId,
                BatchId = batch.Id,
                BatchNo = batch.BatchNo,
                Quantity = line.Quantity,
                EntryAtUtc = DateTime.UtcNow,
                Notes = $"Outgoing purchase return {purchaseReturn.ReturnNo}; {reason.Trim()}"
            });
        }

        unitOfWork.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
        {
            SupplierId = supplier.Id,
            EntryAtUtc = DateTime.UtcNow,
            EntryType = "PurchaseReturn",
            ReferenceId = purchaseReturn.Id,
            ReferenceNo = purchaseReturn.ReturnNo,
            Debit = purchaseReturn.TotalAmount,
            Notes = reason.Trim()
        });
        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = actingUserId,
            Action = "PurchaseReturnPosted",
            EntityName = nameof(PurchaseReturn),
            EntityId = purchaseReturn.Id,
            Details = $"Return {purchaseReturn.ReturnNo}; reason: {reason.Trim()}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return purchaseReturn;
    }

    private static void ValidateHeader(SavePurchaseInput input)
    {
        if (input.SupplierId == Guid.Empty)
        {
            throw new InvalidOperationException("Select a supplier.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(input.InvoiceNo);
        if (input.InvoiceDate > DateOnly.FromDateTime(DateTime.Today))
        {
            throw new InvalidOperationException("Purchase invoice date cannot be in the future.");
        }

        if (input.Items.Count == 0)
        {
            throw new InvalidOperationException("Add at least one purchase item.");
        }

        if (input.DiscountAmount < 0)
        {
            throw new InvalidOperationException("Invoice discount cannot be negative.");
        }

        if (input.DiscountAmount > input.Subtotal)
        {
            throw new InvalidOperationException("Invoice discount cannot exceed the subtotal.");
        }
    }

    private static ValidatedLine ValidateLine(PurchaseLineInput item, Drug drug)
    {
        if (string.IsNullOrWhiteSpace(item.BatchNo) ||
            !item.ExpiryDate.HasValue ||
            item.Quantity <= 0 ||
            item.FreeQuantity < 0 ||
            item.Mrp < 0 ||
            item.Rate < 0 ||
            item.GstRate < 0 ||
            item.DiscountAmount < 0 ||
            item.DiscountAmount > item.Quantity * item.Rate)
        {
            throw new InvalidOperationException($"Purchase line for '{drug.Name}' has invalid batch, expiry, quantity, rate, GST, MRP or discount.");
        }

        if (item.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today))
        {
            throw new InvalidOperationException($"Expired batch {item.BatchNo} cannot be received into saleable stock.");
        }

        var baseAmount = RoundMoney(item.Quantity * item.Rate - item.DiscountAmount);
        var taxAmount = RoundMoney(baseAmount * item.GstRate / 100m);
        var lineTotal = RoundMoney(baseAmount + taxAmount);
        if (RoundMoney(item.Amount) != baseAmount)
        {
            throw new InvalidOperationException(
                $"Purchase amount for '{drug.Name}' must equal quantity × rate less line discount.");
        }

        return new ValidatedLine(item, baseAmount, taxAmount, lineTotal);
    }

    private async Task<CatalogInfo?> GetCatalogInfoAsync(Drug drug, CancellationToken cancellationToken)
    {
        if (drug.CatalogMedicineId.HasValue)
        {
            var linked = await unitOfWork.Context.CatalogInfos
                .FirstOrDefaultAsync(info => info.CatalogMedicineId == drug.CatalogMedicineId, cancellationToken);
            if (linked is not null)
            {
                return linked;
            }
        }

        var nameKey = drug.Name.Trim().ToLowerInvariant();
        return await unitOfWork.Context.CatalogInfos
            .FirstOrDefaultAsync(info => info.NameKey == nameKey, cancellationToken);
    }

    private static string? GetControlledRegisterType(string? schedule, CatalogInfo? info)
    {
        var scheduleValue = schedule?.Trim().ToUpperInvariant();
        if (scheduleValue is "H1" or "X" or "NDPS")
        {
            return scheduleValue;
        }

        return info?.IsHabitForming == true ? "HabitForming" : null;
    }

    private static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ValidatedLine(
        PurchaseLineInput Input,
        decimal BaseAmount,
        decimal TaxAmount,
        decimal LineTotal);
}
