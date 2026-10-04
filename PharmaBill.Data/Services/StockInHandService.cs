using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record StockInHandRow(
    Guid DrugId,
    string DrugName,
    string BatchNo,
    DateOnly? ExpiryDate,
    decimal Opening,
    decimal InQuantity,
    decimal OutQuantity,
    decimal Closing,
    decimal Mrp,
    decimal Ptr,
    decimal PurchaseRate,
    decimal StockValue,
    string Alert);

public sealed record ReorderSuggestion(Guid DrugId, string DrugName, decimal CurrentStock, decimal ReorderLevel, decimal SuggestedQuantity);
public sealed record DeadStockAlert(Guid DrugId, string DrugName, string BatchNo, decimal Quantity, DateTime LastMovementAtUtc);

public sealed class StockInHandService(PharmaBillDbContext context)
{
    public async Task<IReadOnlyList<StockInHandRow>> GetAsOfAsync(
        DateTime asOfUtc,
        DateTime? fromUtc = null,
        DateTime? toUtcExclusive = null,
        string? schedule = null,
        Guid? supplierId = null,
        string? manufacturer = null,
        CancellationToken cancellationToken = default)
    {
        if (asOfUtc.Kind != DateTimeKind.Utc ||
            (fromUtc.HasValue && fromUtc.Value.Kind != DateTimeKind.Utc) ||
            (toUtcExclusive.HasValue && toUtcExclusive.Value.Kind != DateTimeKind.Utc) ||
            (fromUtc.HasValue && toUtcExclusive.HasValue && toUtcExclusive <= fromUtc))
        {
            throw new ArgumentException("Stock report dates must be valid UTC instants.");
        }

        var drugs = await context.Drugs.AsNoTracking()
            .Where(drug => drug.IsActive &&
                           (string.IsNullOrWhiteSpace(schedule) || drug.Schedule == schedule))
            .ToListAsync(cancellationToken);
        var drugIds = drugs.Select(drug => drug.Id).ToArray();
        var batches = await context.Batches.AsNoTracking()
            .Where(batch => drugIds.Contains(batch.DrugId) &&
                            (!supplierId.HasValue || batch.SupplierId == supplierId))
            .ToListAsync(cancellationToken);
        var batchIds = batches.Select(batch => batch.Id).ToArray();
        var movements = await context.StockMovements.AsNoTracking()
            .Where(movement => batchIds.Contains(movement.BatchId) && movement.MovementAtUtc < asOfUtc)
            .ToListAsync(cancellationToken);
        var catalogIds = drugs.Where(drug => drug.CatalogMedicineId.HasValue)
            .Select(drug => drug.CatalogMedicineId!.Value).Distinct().ToArray();
        var catalog = await context.CatalogMedicines.AsNoTracking()
            .Where(item => catalogIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var date = DateOnly.FromDateTime(asOfUtc.ToLocalTime());
        var rows = new List<StockInHandRow>();
        foreach (var drug in drugs)
        {
            var drugBatches = batches.Where(batch => batch.DrugId == drug.Id);
            if (!string.IsNullOrWhiteSpace(manufacturer) &&
                (!drug.CatalogMedicineId.HasValue ||
                 !catalog.TryGetValue(drug.CatalogMedicineId.Value, out var medicine) ||
                 !(medicine.Manufacturer?.Contains(manufacturer, StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                continue;
            }

            foreach (var batch in drugBatches)
            {
                var batchMoves = movements.Where(movement => movement.BatchId == batch.Id).ToArray();
                var before = fromUtc.HasValue
                    ? batchMoves.Where(movement => movement.MovementAtUtc < fromUtc.Value).ToArray()
                    : [];
                var period = batchMoves.Where(movement =>
                    (!fromUtc.HasValue || movement.MovementAtUtc >= fromUtc.Value) &&
                    (!toUtcExclusive.HasValue || movement.MovementAtUtc < toUtcExclusive.Value)).ToArray();
                var opening = before.Sum(movement => movement.QuantityChange);
                var incoming = period.Where(movement => movement.QuantityChange > 0)
                    .Sum(movement => movement.QuantityChange);
                var outgoing = -period.Where(movement => movement.QuantityChange < 0)
                    .Sum(movement => movement.QuantityChange);
                var closing = opening + period.Sum(movement => movement.QuantityChange);
                var alert = GetAlert(batch, drug, closing, date);
                var unitValue = batch.PurchasePrice;
                var value = decimal.Round(closing * unitValue, 2, MidpointRounding.AwayFromZero);
                rows.Add(new StockInHandRow(
                    drug.Id,
                    drug.Name,
                    batch.BatchNo,
                    batch.ExpiryDate,
                    opening,
                    incoming,
                    outgoing,
                    closing,
                    batch.Mrp ?? drug.Mrp ?? 0m,
                    batch.Ptr ?? 0m,
                    batch.PurchasePrice,
                    value,
                    alert));
            }
        }

        return rows.OrderBy(row => row.DrugName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(row => row.BatchNo, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<ReorderSuggestion>> GetReorderSuggestionsAsync(
        CancellationToken cancellationToken = default)
    {
        var drugs = await context.Drugs.AsNoTracking()
            .Where(drug => drug.IsActive && drug.ReorderLevel > 0)
            .ToListAsync(cancellationToken);
        var batchIds = await context.Batches.AsNoTracking()
            .Where(batch => drugs.Select(drug => drug.Id).Contains(batch.DrugId))
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken);
        var batches = await context.Batches.AsNoTracking()
            .Where(batch => batchIds.Contains(batch.Id))
            .ToListAsync(cancellationToken);
        var stock = await context.StockMovements.AsNoTracking()
            .Where(movement => batchIds.Contains(movement.BatchId))
            .GroupBy(movement => movement.BatchId)
            .Select(group => new { BatchId = group.Key, Quantity = group.Sum(movement => movement.QuantityChange) })
            .ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
        return drugs.Select(drug =>
            {
                var current = batches.Where(batch => batch.DrugId == drug.Id)
                    .Sum(batch => stock.GetValueOrDefault(batch.Id));
                return new ReorderSuggestion(
                    drug.Id,
                    drug.Name,
                    current,
                    drug.ReorderLevel,
                    Math.Max(0m, drug.ReorderLevel - current));
            })
            .Where(item => item.SuggestedQuantity > 0)
            .OrderByDescending(item => item.SuggestedQuantity)
            .ToArray();
    }

    public async Task<IReadOnlyList<DeadStockAlert>> GetDeadStockAsync(
        DateTime inactiveBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        if (inactiveBeforeUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Dead stock cutoff must be UTC.", nameof(inactiveBeforeUtc));
        }

        var batches = await context.Batches.AsNoTracking().ToListAsync(cancellationToken);
        var batchIds = batches.Select(item => item.Id).ToArray();
        var movements = await context.StockMovements.AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId))
            .ToListAsync(cancellationToken);
        var drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken);
        return batches.Select(batch =>
            {
                var batchMovements = movements.Where(item => item.BatchId == batch.Id).ToArray();
                var lastMovement = batchMovements.OrderByDescending(item => item.MovementAtUtc)
                    .Select(item => (DateTime?)item.MovementAtUtc).FirstOrDefault();
                return new DeadStockAlert(
                    batch.DrugId,
                    drugs[batch.DrugId].Name,
                    batch.BatchNo,
                    batchMovements.Sum(item => item.QuantityChange),
                    lastMovement ?? DateTime.MinValue);
            })
            .Where(item => item.Quantity > 0 && item.LastMovementAtUtc < inactiveBeforeUtc)
            .OrderBy(item => item.LastMovementAtUtc)
            .ThenBy(item => item.DrugName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetAlert(Batch batch, Drug drug, decimal closing, DateOnly asOfDate)
    {
        if (closing < 0)
        {
            return "Negative stock";
        }

        if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < asOfDate)
        {
            return "Expired";
        }

        if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value.DayNumber - asOfDate.DayNumber < 90)
        {
            return "Near expiry";
        }

        return closing < drug.ReorderLevel ? "Reorder" : string.Empty;
    }
}
