using System;

namespace PharmaBill.Data.Services;

public sealed record SubstituteStockResult(Guid CatalogMedicineId, Guid? DrugId, string BrandName, string? SaltStrength, string CompositionKey, decimal CurrentStock, decimal? Mrp, string? Rack, string? PackSize, string? Manufacturer, bool IsHabitForming)
{
	public bool IsInStock => CurrentStock > 0m;
}
