using System;

namespace PharmaBill.Data.Services;

public sealed record PurchaseIndentItem(Guid DrugId, string DrugName, string? Schedule, decimal CurrentStock, decimal ReorderLevel, decimal TargetStockLevel, decimal SuggestedOrderQty, Guid? SupplierId, string? SupplierName, decimal? LastPurchaseRate, decimal? LastMrp);
