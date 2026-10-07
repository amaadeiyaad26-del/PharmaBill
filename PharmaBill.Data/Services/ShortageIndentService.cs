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

public sealed class ShortageIndentService(PharmaBillDbContext context)
{
	public async Task<List<PurchaseIndentItem>> GetAutomatedShortageIndentAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		List<Drug> drugs = await (from drug2 in context.Drugs.AsNoTracking()
			where drug2.IsActive && drug2.ReorderLevel > 0m
			select drug2).ToListAsync(cancellationToken);
		if (drugs.Count == 0)
		{
			return new List<PurchaseIndentItem>();
		}
		Guid[] drugIds = drugs.Select((Drug drug2) => drug2.Id).ToArray();
		List<Batch> batches = await (from batch2 in context.Batches.AsNoTracking()
			where drugIds.Contains(batch2.DrugId)
			select batch2).ToListAsync(cancellationToken);
		Guid[] batchIds = batches.Select((Batch batch2) => batch2.Id).ToArray();
		Dictionary<Guid, decimal> dictionary = ((batchIds.Length != 0) ? (await (from movement in context.StockMovements.AsNoTracking()
			where batchIds.Contains(movement.BatchId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement item) => item.QuantityChange)
			}).ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken)) : new Dictionary<Guid, decimal>());
		Dictionary<Guid, decimal> movements = dictionary;
		Guid[] supplierIds = (from batch2 in batches
			where batch2.SupplierId.HasValue
			select batch2.SupplierId.Value).Distinct().ToArray();
		Dictionary<Guid, string> dictionary2 = ((supplierIds.Length != 0) ? (await (from supplier in context.Suppliers.AsNoTracking()
			where supplierIds.Contains(supplier.Id)
			select supplier).ToDictionaryAsync((Supplier supplier) => supplier.Id, (Supplier supplier) => supplier.Name, cancellationToken)) : new Dictionary<Guid, string>());
		Dictionary<Guid, string> dictionary3 = dictionary2;
		List<PurchaseIndentItem> list = new List<PurchaseIndentItem>();
		foreach (Drug drug in drugs.OrderBy((Drug item) => item.Name, StringComparer.OrdinalIgnoreCase))
		{
			Batch[] array = batches.Where((Batch batch2) => batch2.DrugId == drug.Id).ToArray();
			decimal currentStock = array.Sum((Batch batch2) => movements.GetValueOrDefault(batch2.Id));
			if (!StockShortage.IsShortage(currentStock, drug.ReorderLevel))
			{
				continue;
			}
			decimal num = StockShortage.SuggestedOrderQuantity(currentStock, drug.ReorderLevel);
			if (!(num <= 0m))
			{
				Guid? guid = ResolvePrimarySupplier(array, movements);
				dictionary3.TryGetValue(guid ?? Guid.Empty, out var value);
				Batch batch = (from batch2 in array
					where movements.GetValueOrDefault(batch2.Id) > 0m || batch2.PurchasePrice > 0m
					orderby batch2.UpdatedAtUtc descending
					select batch2).FirstOrDefault() ?? array.OrderByDescending((Batch batch2) => batch2.UpdatedAtUtc).FirstOrDefault();
				list.Add(new PurchaseIndentItem(drug.Id, drug.Name, drug.Schedule, currentStock, drug.ReorderLevel, StockShortage.ResolveTargetStockLevel(drug.ReorderLevel), num, guid, guid.HasValue ? (value ?? "Unknown supplier") : null, batch?.PurchasePrice, batch?.Mrp ?? drug.Mrp));
			}
		}
		return list;
	}

	public async Task<IReadOnlyList<PurchaseIndentGroup>> GetIndentGroupedBySupplierAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return (from item in await GetAutomatedShortageIndentAsync(cancellationToken)
			group item by item.SupplierId into @group
			select new PurchaseIndentGroup(@group.Key, @group.Key.HasValue ? (@group.Select((PurchaseIndentItem item) => item.SupplierName).FirstOrDefault((string name) => !string.IsNullOrWhiteSpace(name)) ?? "Unknown supplier") : "General requisition (no supplier mapped)", @group.OrderBy((PurchaseIndentItem item) => item.DrugName, StringComparer.OrdinalIgnoreCase).ToArray()) into @group
			orderby (!@group.SupplierId.HasValue) ? 1 : 0
			select @group).ThenBy((PurchaseIndentGroup group) => group.SupplierName, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public static string FormatIndentText(IReadOnlyList<PurchaseIndentItem> items)
	{
		if (items.Count == 0)
		{
			return "No shortage items.";
		}
		List<string> list = new List<string>
		{
			$"Purchase indent — {DateTime.Now:dd-MMM-yyyy HH:mm}",
			"Medicine | On hand | Reorder | Target | Order qty | Supplier"
		};
		foreach (PurchaseIndentItem item in items)
		{
			list.Add($"{item.DrugName} | {item.CurrentStock:0.##} | {item.ReorderLevel:0.##} | {item.TargetStockLevel:0.##} | {item.SuggestedOrderQty:0.##} | {item.SupplierName ?? "General"}");
		}
		list.Add($"Total lines: {items.Count}");
		list.Add($"Total suggested units: {items.Sum((PurchaseIndentItem item) => item.SuggestedOrderQty):0.##}");
		return string.Join(Environment.NewLine, list);
	}

	private static Guid? ResolvePrimarySupplier(IReadOnlyList<Batch> batches, IReadOnlyDictionary<Guid, decimal> movements)
	{
		return (from batch in batches
			where batch.SupplierId.HasValue
			group batch by batch.SupplierId.Value into @group
			select new
			{
				SupplierId = @group.Key,
				Quantity = @group.Sum((Batch batch) => Math.Max(0m, movements.GetValueOrDefault(batch.Id))),
				Latest = @group.Max((Batch batch) => batch.UpdatedAtUtc)
			} into item
			orderby item.Quantity descending, item.Latest descending
			select (Guid?)item.SupplierId).FirstOrDefault();
	}
}
