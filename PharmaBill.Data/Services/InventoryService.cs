using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record StockBatchRow(
    Guid BatchId,
    string BatchNo,
    DateOnly? ExpiryDate,
    decimal Mrp,
    decimal? Ptr,
    decimal PurchaseRate,
    decimal Quantity,
    string? Rack)
{
    public string ExpiryStatus
    {
        get
        {
            if (!ExpiryDate.HasValue || ExpiryDate.Value >= DateOnly.FromDateTime(DateTime.Today))
            {
                return ExpiryDate.HasValue &&
                       ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today).AddDays(90)
                    ? "Near expiry"
                    : "Active";
            }

            return "Expired";
        }
    }
}

public sealed record StockDrugRow(
    Guid DrugId,
    string DrugName,
    string? Schedule,
    string? Manufacturer,
    string? Supplier,
    decimal TotalStock,
    decimal ReorderLevel,
    IReadOnlyList<StockBatchRow> Batches)
{
    public bool IsBelowReorderLevel => TotalStock < ReorderLevel;
}

public sealed record ExpiredBatchRow(
    Guid BatchId,
    string DrugName,
    string BatchNo,
    DateOnly ExpiryDate,
    decimal Quantity);

public sealed class InventoryService(
    PharmaBillDbContext context,
    IUnitOfWork unitOfWork,
    IEntitlementService entitlement)
{
    public async Task<IReadOnlyList<StockDrugRow>> GetStockAsync(
        string? schedule = null,
        string? company = null,
        Guid? supplierId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var drugs = await context.Drugs.AsNoTracking()
            .Where(drug => drug.IsActive)
            .ToListAsync(cancellationToken);
        var drugIds = drugs.Select(drug => drug.Id).ToArray();
        var batches = await context.Batches.AsNoTracking()
            .Where(batch => drugIds.Contains(batch.DrugId))
            .ToListAsync(cancellationToken);
        var batchIds = batches.Select(batch => batch.Id).ToArray();
        var movementQuantities = await context.StockMovements.AsNoTracking()
            .Where(movement => batchIds.Contains(movement.BatchId))
            .GroupBy(movement => movement.BatchId)
            .Select(group => new
            {
                BatchId = group.Key,
                Quantity = group.Sum(movement => movement.QuantityChange)
            })
            .ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
        var supplierIds = batches.Where(batch => batch.SupplierId.HasValue)
            .Select(batch => batch.SupplierId!.Value)
            .Distinct()
            .ToArray();
        var suppliers = await context.Suppliers.AsNoTracking()
            .Where(supplier => supplierIds.Contains(supplier.Id))
            .ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, cancellationToken);
        var batchesByDrug = batches
            .GroupBy(batch => batch.DrugId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var catalogIds = drugs.Where(drug => drug.CatalogMedicineId.HasValue)
            .Select(drug => drug.CatalogMedicineId!.Value)
            .Distinct()
            .ToArray();
        var manufacturers = await context.CatalogMedicines.AsNoTracking()
            .Where(medicine => catalogIds.Contains(medicine.Id))
            .ToDictionaryAsync(medicine => medicine.Id, medicine => medicine.Manufacturer, cancellationToken);

        return drugs.Select(drug =>
            {
                var drugBatches = batchesByDrug.GetValueOrDefault(drug.Id, []);
                var total = drugBatches.Sum(batch => movementQuantities.GetValueOrDefault(batch.Id));
                var manufacturer = drug.CatalogMedicineId.HasValue
                    ? manufacturers.GetValueOrDefault(drug.CatalogMedicineId.Value)
                    : null;
                var row = new StockDrugRow(
                    drug.Id,
                    drug.Name,
                    drug.Schedule,
                    manufacturer,
                    drugBatches.Select(batch => batch.SupplierId.HasValue
                            ? suppliers.GetValueOrDefault(batch.SupplierId.Value)
                            : null)
                        .FirstOrDefault(name => name is not null),
                    total,
                    drug.ReorderLevel,
                    drugBatches.OrderBy(batch => batch.ExpiryDate ?? DateOnly.MaxValue)
                        .ThenBy(batch => batch.BatchNo, StringComparer.OrdinalIgnoreCase)
                        .Select(batch => new StockBatchRow(
                            batch.Id,
                            batch.BatchNo,
                            batch.ExpiryDate,
                            batch.Mrp ?? 0,
                            batch.Ptr,
                            batch.PurchasePrice,
                            movementQuantities.GetValueOrDefault(batch.Id),
                            batch.Rack))
                        .ToArray());
                return row;
            })
            .Where(row => string.IsNullOrWhiteSpace(schedule) ||
                          string.Equals(row.Schedule, schedule, StringComparison.OrdinalIgnoreCase))
            .Where(row => string.IsNullOrWhiteSpace(company) ||
                          (row.Manufacturer?.Contains(company, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(row => !supplierId.HasValue ||
                          (batchesByDrug.TryGetValue(row.DrugId, out var matchingBatches) &&
                           matchingBatches.Any(batch => batch.SupplierId == supplierId.Value)))
            .Where(row => status switch
            {
                "Expired" => row.Batches.Any(batch => batch.ExpiryDate < DateOnly.FromDateTime(DateTime.Today)),
                "Near expiry" => row.Batches.Any(batch =>
                    batch.ExpiryDate >= DateOnly.FromDateTime(DateTime.Today) &&
                    batch.ExpiryDate < DateOnly.FromDateTime(DateTime.Today).AddDays(90)),
                "Reorder" => row.TotalStock < row.ReorderLevel,
                "In stock" => row.TotalStock > 0,
                "Out of stock" => row.TotalStock <= 0,
                _ => true
            })
            .OrderBy(row => row.DrugName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task AdjustStockAsync(
        Guid batchId,
        decimal quantityChange,
        string reason,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionMatrix.Allows(role, AppPermission.EnterStock))
        {
            throw new UnauthorizedAccessException("Your role cannot adjust stock.");
        }

        if (!await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken))
        {
            throw new InvalidOperationException("Stock entry is unavailable in read-only mode.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (quantityChange == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityChange), "Adjustment quantity cannot be zero.");
        }

        var batch = await context.Batches.SingleAsync(item => item.Id == batchId, cancellationToken);
        var onHand = await context.StockMovements
            .Where(movement => movement.BatchId == batchId)
            .SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken) ?? 0;
        if (onHand + quantityChange < 0)
        {
            throw new InvalidOperationException("Adjustment cannot reduce batch stock below zero.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        batch.Quantity = onHand + quantityChange;
        var adjustment = new StockAdjustment
        {
            BatchId = batch.Id,
            DrugId = batch.DrugId,
            QuantityChange = quantityChange,
            Reason = reason.Trim()
        };
        context.StockAdjustments.Add(adjustment);
        context.StockMovements.Add(new StockMovement
        {
            BatchId = batch.Id,
            DrugId = batch.DrugId,
            QuantityChange = quantityChange,
            MovementType = "Adjustment",
            ReferenceType = nameof(StockAdjustment),
            ReferenceId = adjustment.Id,
            Notes = reason.Trim()
        });
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "StockAdjusted",
            EntityName = nameof(Batch),
            EntityId = batch.Id,
            Details = $"Quantity change: {quantityChange}; reason: {reason.Trim()}"
        });
        if (quantityChange > 0)
        {
            var stockConflict = await context.SyncConflicts.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item =>
                    item.EntityName == "StockConflict" && item.EntityId == batch.Id && item.Resolution == "Open",
                    cancellationToken);
            if (stockConflict is not null)
            {
                var resolvedAt = DateTime.UtcNow;
                stockConflict.Resolution = $"Resolved by stock adjustment: {reason.Trim()}";
                stockConflict.ResolvedAtUtc = resolvedAt;
                stockConflict.UpdatedAtUtc = resolvedAt;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExpiredBatchRow>> GetExpiredBatchesAsync(
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var expired = await context.Batches.AsNoTracking()
            .Where(batch => batch.ExpiryDate != null && batch.ExpiryDate < today)
            .ToListAsync(cancellationToken);
        var ids = expired.Select(batch => batch.Id).ToArray();
        var quantities = await context.StockMovements.AsNoTracking()
            .Where(movement => ids.Contains(movement.BatchId))
            .GroupBy(movement => movement.BatchId)
            .Select(group => new { BatchId = group.Key, Quantity = group.Sum(movement => movement.QuantityChange) })
            .ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
        var drugIds = expired.Select(batch => batch.DrugId).Distinct().ToArray();
        var names = await context.Drugs.AsNoTracking()
            .Where(drug => drugIds.Contains(drug.Id))
            .ToDictionaryAsync(drug => drug.Id, drug => drug.Name, cancellationToken);
        return expired
            .Select(batch => new ExpiredBatchRow(
                batch.Id,
                names.GetValueOrDefault(batch.DrugId, string.Empty),
                batch.BatchNo,
                batch.ExpiryDate!.Value,
                quantities.GetValueOrDefault(batch.Id)))
            .Where(row => row.Quantity > 0)
            .OrderBy(row => row.DrugName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ExpiryDate)
            .ToArray();
    }

    // Writes off every expired batch that still has stock, all in one transaction.
    public async Task<int> WriteOffAllExpiredAsync(
        string reason,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionMatrix.Allows(role, AppPermission.EnterStock))
        {
            throw new UnauthorizedAccessException("Your role cannot write off stock.");
        }

        if (!await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken))
        {
            throw new InvalidOperationException("Stock entry is unavailable in read-only mode.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var rows = await GetExpiredBatchesAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return 0;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        foreach (var row in rows)
        {
            var batch = await context.Batches.SingleAsync(item => item.Id == row.BatchId, cancellationToken);
            batch.Quantity = 0;
            context.ExpiryWriteOffs.Add(new ExpiryWriteOff
            {
                BatchId = batch.Id,
                DrugId = batch.DrugId,
                Quantity = row.Quantity,
                ExpiryDate = row.ExpiryDate,
                Reason = reason.Trim()
            });
            context.StockMovements.Add(new StockMovement
            {
                BatchId = batch.Id,
                DrugId = batch.DrugId,
                QuantityChange = -row.Quantity,
                MovementType = "ExpiryWriteOff",
                ReferenceType = nameof(ExpiryWriteOff),
                ReferenceId = batch.Id,
                Notes = reason.Trim()
            });
            context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
            {
                RegisterType = "ExpiryDump",
                DrugId = batch.DrugId,
                BatchId = batch.Id,
                Quantity = row.Quantity,
                BatchNo = batch.BatchNo,
                EntryAtUtc = DateTime.UtcNow,
                Notes = reason.Trim()
            });
            context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "ExpiredStockWrittenOff",
                EntityName = nameof(Batch),
                EntityId = batch.Id,
                Details = $"Quantity: {row.Quantity}; reason: {reason.Trim()}"
            });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows.Count;
    }

    public async Task WriteOffExpiredBatchAsync(
        Guid batchId,
        decimal quantity,
        string reason,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        if (!PermissionMatrix.Allows(role, AppPermission.EnterStock))
        {
            throw new UnauthorizedAccessException("Your role cannot write off stock.");
        }

        if (!await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken))
        {
            throw new InvalidOperationException("Stock entry is unavailable in read-only mode.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        var batch = await context.Batches.SingleAsync(item => item.Id == batchId, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (!batch.ExpiryDate.HasValue || batch.ExpiryDate.Value >= today)
        {
            throw new InvalidOperationException("Only expired batches can be written off.");
        }

        var onHand = await context.StockMovements
            .Where(movement => movement.BatchId == batchId)
            .SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken) ?? 0;
        if (quantity > onHand)
        {
            throw new InvalidOperationException("Write-off quantity exceeds current batch stock.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        batch.Quantity = onHand - quantity;
        context.ExpiryWriteOffs.Add(new ExpiryWriteOff
        {
            BatchId = batch.Id,
            DrugId = batch.DrugId,
            Quantity = quantity,
            ExpiryDate = batch.ExpiryDate.Value,
            Reason = reason.Trim()
        });
        context.StockMovements.Add(new StockMovement
        {
            BatchId = batch.Id,
            DrugId = batch.DrugId,
            QuantityChange = -quantity,
            MovementType = "ExpiryWriteOff",
            ReferenceType = nameof(ExpiryWriteOff),
            ReferenceId = batch.Id,
            Notes = reason.Trim()
        });
        context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
        {
            RegisterType = "ExpiryDump",
            DrugId = batch.DrugId,
            BatchId = batch.Id,
            Quantity = quantity,
            BatchNo = batch.BatchNo,
            EntryAtUtc = DateTime.UtcNow,
            Notes = reason.Trim()
        });
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "ExpiredStockWrittenOff",
            EntityName = nameof(Batch),
            EntityId = batch.Id,
            Details = $"Quantity: {quantity}; reason: {reason.Trim()}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
