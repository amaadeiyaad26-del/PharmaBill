using System;

namespace PharmaBill.App.ViewModels;

/// <summary>
/// Unified wholesale medicine search row: in-stock batch, out-of-stock batch, or master catalogue (needs inward).
/// </summary>
public sealed class WholesaleMedicinePickerItem
{
	public required string GroupKey { get; init; }

	public required string DisplayTitle { get; init; }

	public required string DisplayDetail { get; init; }

	public required string Display { get; init; }

	public WholesaleStockChoice? StockChoice { get; init; }

	public Guid? DrugId { get; init; }

	public Guid? CatalogMedicineId { get; init; }

	public string MedicineName { get; init; } = string.Empty;

	public string? Composition { get; init; }

	public string? Manufacturer { get; init; }

	public decimal? SuggestedMrp { get; init; }

	public bool IsInStock => StockChoice is { Available: > 0m };

	public bool NeedsInward => !IsInStock;

	public static WholesaleMedicinePickerItem FromInStock(WholesaleStockChoice choice)
	{
		string ptr = choice.Ptr is > 0m ? $"PTR Rs {choice.Ptr:0.##}" : "PTR -";
		string detail = $"Batch {choice.BatchNo} | Exp {FormatExpiry(choice.ExpiryDate)} | Stock {choice.Available:0.##} | {ptr} | MRP Rs {choice.Mrp:0.##}";
		return new WholesaleMedicinePickerItem
		{
			GroupKey = "In stock (FEFO)",
			DisplayTitle = choice.DrugName,
			DisplayDetail = detail,
			Display = $"[In stock] {choice.DrugName} — {detail}",
			StockChoice = choice,
			DrugId = choice.DrugId,
			MedicineName = choice.DrugName,
			SuggestedMrp = choice.Mrp
		};
	}

	public static WholesaleMedicinePickerItem FromOutOfStock(WholesaleStockChoice choice)
	{
		string detail = $"Batch {choice.BatchNo} | Exp {FormatExpiry(choice.ExpiryDate)} | Out of Stock - Click to Add Stock / Batch";
		return new WholesaleMedicinePickerItem
		{
			GroupKey = "Out of stock",
			DisplayTitle = choice.DrugName,
			DisplayDetail = detail,
			Display = $"[Out of stock] {choice.DrugName} — {detail}",
			StockChoice = choice,
			DrugId = choice.DrugId,
			MedicineName = choice.DrugName,
			SuggestedMrp = choice.Mrp
		};
	}

	public static WholesaleMedicinePickerItem FromCatalogue(Guid catalogMedicineId, string name, string? composition, string? manufacturer, decimal? mrp, Guid? drugId)
	{
		string detail = "Master Drug Bank - 0 stock. + Add Batch to Stock";
		return new WholesaleMedicinePickerItem
		{
			GroupKey = "Master Drug Bank Catalogue",
			DisplayTitle = name,
			DisplayDetail = detail,
			Display = $"[Catalogue] {name} — {detail}",
			CatalogMedicineId = catalogMedicineId,
			DrugId = drugId,
			MedicineName = name,
			Composition = composition,
			Manufacturer = manufacturer,
			SuggestedMrp = mrp
		};
	}

	private static string FormatExpiry(DateOnly? expiry)
	{
		return expiry.HasValue ? expiry.Value.ToString("MM/yyyy") : "-";
	}
}