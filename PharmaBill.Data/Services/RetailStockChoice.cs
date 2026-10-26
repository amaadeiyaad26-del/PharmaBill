using PharmaBill.Core.Gst;

namespace PharmaBill.Data.Services;

public sealed record RetailStockChoice(Guid DrugId, string DrugName, string? Barcode, Guid BatchId, string BatchNo, DateOnly? ExpiryDate, decimal Mrp, decimal? SalePrice, decimal AvailableQuantity, decimal? GstRate, string? Schedule, bool IsHabitForming, string? RegisterType, bool IsBanned = false, string? HsnCode = null, bool IsNilRated = false, string? Composition = null, string? Manufacturer = null, string? PackLabel = null)
{
	/// <summary>The product's recorded HSN and GST rate, or null when either is not known (never treated as 0%).</summary>
	public ProductTaxDetails? KnownTax => ProductTax.Known(HsnCode, GstRate, IsNilRated);

	/// <summary>The search flyout's third line: maker and pack size, whichever are on record.</summary>
	public string MakerAndPack => string.Join(" • ", new[] { Manufacturer, PackLabel }.Where(part => !string.IsNullOrWhiteSpace(part)));

	/// <summary>Batch, expiry and stock on hand: what the counter needs to pick a batch.</summary>
	public string BatchSummary => $"Batch {BatchNo} · Exp {(ExpiryDate.HasValue ? ExpiryDate.Value.ToString("MM/yy") : "-")} · Stock {AvailableQuantity:0.##}";
}
