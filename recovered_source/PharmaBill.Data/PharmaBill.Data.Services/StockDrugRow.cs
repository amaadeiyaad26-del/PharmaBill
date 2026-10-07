using System;
using System.Collections.Generic;
using PharmaBill.Core.Inventory;

namespace PharmaBill.Data.Services;

public sealed record StockDrugRow(Guid DrugId, string DrugName, string? Schedule, string? Manufacturer, string? Supplier, decimal TotalStock, decimal ReorderLevel, IReadOnlyList<StockBatchRow> Batches)
{
	public bool IsShortage => StockShortage.IsShortage(TotalStock, ReorderLevel);

	public bool IsBelowReorderLevel => IsShortage;

	public decimal TargetStockLevel => StockShortage.ResolveTargetStockLevel(ReorderLevel);

	public decimal SuggestedOrderQuantity => StockShortage.SuggestedOrderQuantity(TotalStock, ReorderLevel);
}
