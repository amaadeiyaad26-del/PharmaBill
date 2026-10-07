using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class StockInHandService(PharmaBillDbContext context)
{
	public async Task<IReadOnlyList<StockInHandRow>> GetAsOfAsync(DateTime asOfUtc, DateTime? fromUtc = null, DateTime? toUtcExclusive = null, string? schedule = null, Guid? supplierId = null, string? manufacturer = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (asOfUtc.Kind != DateTimeKind.Utc || (fromUtc.HasValue && fromUtc.Value.Kind != DateTimeKind.Utc) || (toUtcExclusive.HasValue && toUtcExclusive.Value.Kind != DateTimeKind.Utc) || (fromUtc.HasValue && toUtcExclusive.HasValue && toUtcExclusive <= fromUtc))
		{
			throw new ArgumentException("Stock report dates must be valid UTC instants.");
		}
		List<Drug> drugs = await (from drug2 in context.Drugs.AsNoTracking()
			where drug2.IsActive && (string.IsNullOrWhiteSpace(schedule) || drug2.Schedule == schedule)
			select drug2).ToListAsync(cancellationToken);
		Guid[] drugIds = drugs.Select((Drug drug2) => drug2.Id).ToArray();
		List<Batch> batches = await (from batch2 in context.Batches.AsNoTracking()
			where drugIds.Contains(batch2.DrugId) && (!((Guid?)supplierId).HasValue || batch2.SupplierId == supplierId)
			select batch2).ToListAsync(cancellationToken);
		Guid[] batchIds = batches.Select((Batch batch2) => batch2.Id).ToArray();
		List<StockMovement> movements = await (from movement in context.StockMovements.AsNoTracking()
			where batchIds.Contains(movement.BatchId) && movement.MovementAtUtc < asOfUtc
			select movement).ToListAsync(cancellationToken);
		Guid[] catalogIds = (from drug2 in drugs
			where drug2.CatalogMedicineId.HasValue
			select drug2.CatalogMedicineId.Value).Distinct().ToArray();
		Dictionary<Guid, CatalogMedicine> dictionary = await (from item in context.CatalogMedicines.AsNoTracking()
			where catalogIds.Contains(item.Id)
			select item).ToDictionaryAsync((CatalogMedicine item) => item.Id, cancellationToken);
		DateOnly asOfDate = DateOnly.FromDateTime(asOfUtc.ToLocalTime());
		List<StockInHandRow> list = new List<StockInHandRow>();
		foreach (Drug drug in drugs)
		{
			IEnumerable<Batch> enumerable = batches.Where((Batch batch2) => batch2.DrugId == drug.Id);
			if (!string.IsNullOrWhiteSpace(manufacturer) && (!drug.CatalogMedicineId.HasValue || !dictionary.TryGetValue(drug.CatalogMedicineId.Value, out var value) || !(value.Manufacturer?.Contains(manufacturer, StringComparison.OrdinalIgnoreCase) ?? false)))
			{
				continue;
			}
			foreach (Batch batch in enumerable)
			{
				StockMovement[] source = movements.Where((StockMovement movement) => movement.BatchId == batch.Id).ToArray();
				StockMovement[] source2 = (fromUtc.HasValue ? source.Where((StockMovement movement) => movement.MovementAtUtc < fromUtc.Value).ToArray() : Array.Empty<StockMovement>());
				StockMovement[] source3 = source.Where((StockMovement movement) => (!fromUtc.HasValue || movement.MovementAtUtc >= fromUtc.Value) && (!toUtcExclusive.HasValue || movement.MovementAtUtc < toUtcExclusive.Value)).ToArray();
				decimal num = source2.Sum((StockMovement movement) => movement.QuantityChange);
				decimal inQuantity = source3.Where((StockMovement movement) => movement.QuantityChange > 0m).Sum((StockMovement movement) => movement.QuantityChange);
				decimal outQuantity = -source3.Where((StockMovement movement) => movement.QuantityChange < 0m).Sum((StockMovement movement) => movement.QuantityChange);
				decimal num2 = num + source3.Sum((StockMovement movement) => movement.QuantityChange);
				string alert = GetAlert(batch, drug, num2, asOfDate);
				decimal purchasePrice = batch.PurchasePrice;
				decimal stockValue = decimal.Round(num2 * purchasePrice, 2, MidpointRounding.AwayFromZero);
				list.Add(new StockInHandRow(drug.Id, drug.Name, batch.BatchNo, batch.ExpiryDate, num, inQuantity, outQuantity, num2, batch.Mrp ?? drug.Mrp.GetValueOrDefault(), batch.Ptr.GetValueOrDefault(), batch.PurchasePrice, stockValue, alert));
			}
		}
		return list.OrderBy((StockInHandRow row) => row.DrugName, StringComparer.OrdinalIgnoreCase).ThenBy((StockInHandRow row) => row.ExpiryDate ?? DateOnly.MaxValue).ThenBy((StockInHandRow row) => row.BatchNo, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public async Task<IReadOnlyList<ReorderSuggestion>> GetReorderSuggestionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		List<Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive && drug.ReorderLevel > 0m
			select drug).ToListAsync(cancellationToken);
		List<Guid> batchIds = await (from batch in context.Batches.AsNoTracking()
			where drugs.Select((Drug drug) => drug.Id).Contains(batch.DrugId)
			select batch.Id).ToListAsync(cancellationToken);
		List<Batch> batches = await (from batch in context.Batches.AsNoTracking()
			where batchIds.Contains(batch.Id)
			select batch).ToListAsync(cancellationToken);
		Dictionary<Guid, decimal> stock = await (from movement in context.StockMovements.AsNoTracking()
			where batchIds.Contains(movement.BatchId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement movement) => movement.QuantityChange)
			}).ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
		return (from item in drugs.Select((Drug drug) =>
			{
				decimal currentStock = batches.Where((Batch batch) => batch.DrugId == drug.Id).Sum((Batch batch) => stock.GetValueOrDefault(batch.Id));
				return new ReorderSuggestion(drug.Id, drug.Name, currentStock, drug.ReorderLevel, StockShortage.SuggestedOrderQuantity(currentStock, drug.ReorderLevel));
			})
			where item.SuggestedQuantity > 0m
			orderby item.SuggestedQuantity descending
			select item).ToArray();
	}

	public async Task<IReadOnlyList<DeadStockAlert>> GetDeadStockAsync(DateTime inactiveBeforeUtc, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (inactiveBeforeUtc.Kind != DateTimeKind.Utc)
		{
			throw new ArgumentException("Dead stock cutoff must be UTC.", "inactiveBeforeUtc");
		}
		List<Batch> batches = await context.Batches.AsNoTracking().ToListAsync(cancellationToken);
		Guid[] batchIds = batches.Select((Batch item) => item.Id).ToArray();
		List<StockMovement> movements = await (from item in context.StockMovements.AsNoTracking()
			where batchIds.Contains(item.BatchId)
			select item).ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, cancellationToken);
		return (from item in batches.Select((Batch batch) =>
			{
				StockMovement[] source = movements.Where((StockMovement item) => item.BatchId == batch.Id).ToArray();
				DateTime? dateTime = ((IEnumerable<StockMovement>)source.OrderByDescending((StockMovement item) => item.MovementAtUtc)).Select((Func<StockMovement, DateTime?>)((StockMovement item) => item.MovementAtUtc)).FirstOrDefault();
				return new DeadStockAlert(batch.DrugId, drugs[batch.DrugId].Name, batch.BatchNo, source.Sum((StockMovement item) => item.QuantityChange), dateTime ?? DateTime.MinValue);
			})
			where item.Quantity > 0m && item.LastMovementAtUtc < inactiveBeforeUtc
			orderby item.LastMovementAtUtc
			select item).ThenBy((DeadStockAlert item) => item.DrugName, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static string GetAlert(Batch batch, Drug drug, decimal closing, DateOnly asOfDate)
	{
		if (closing < 0m)
		{
			return "Negative stock";
		}
		if (batch.ExpiryDate.HasValue)
		{
			BatchExpiryStatus batchExpiryStatus = BatchExpiryStatuses.Evaluate(batch.ExpiryDate, asOfDate);
			if (batchExpiryStatus == BatchExpiryStatus.Critical && batch.ExpiryDate.Value < asOfDate)
			{
				return "Expired";
			}
			if ((uint)(batchExpiryStatus - 1) <= 1u)
			{
				return "Near expiry";
			}
		}
		if (!StockShortage.IsShortage(closing, drug.ReorderLevel))
		{
			return string.Empty;
		}
		return "Reorder";
	}
}
