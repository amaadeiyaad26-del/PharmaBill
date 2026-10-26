
namespace PharmaBill.Data.Services;

public sealed record MedicineSearchResult(Guid CatalogMedicineId, string Name, string? Composition, string CompositionKey, string? Manufacturer, string? PackSize, decimal? ReferencePrice, bool IsHabitForming, decimal StockQuantity, bool IsInStock, DateOnly? NearestExpiry = null, string? Rack = null, decimal? Mrp = null, Guid? DrugId = null, string? OtherBranchAvailability = null)
{
	/// <summary>The search flyout's third line: maker and pack size, whichever are on record.</summary>
	public string MakerAndPack => string.Join(" • ", new[] { Manufacturer, PackSize }.Where(part => !string.IsNullOrWhiteSpace(part)));

	/// <summary>The price shown on the flyout row: the shop's MRP when stocked, else the drug bank's reference price.</summary>
	public decimal? DisplayPrice => Mrp ?? ReferencePrice;
}
