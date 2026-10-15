using System;
using PharmaBill.Core.Gst;

namespace PharmaBill.Data.Services;

public sealed record RetailStockChoice(Guid DrugId, string DrugName, string? Barcode, Guid BatchId, string BatchNo, DateOnly? ExpiryDate, decimal Mrp, decimal? SalePrice, decimal AvailableQuantity, decimal? GstRate, string? Schedule, bool IsHabitForming, string? RegisterType, bool IsBanned = false, string? HsnCode = null, bool IsNilRated = false)
{
	/// <summary>The product's recorded HSN and GST rate, or null when either is not known (never treated as 0%).</summary>
	public ProductTaxDetails? KnownTax => ProductTax.Known(HsnCode, GstRate, IsNilRated);
}
