
namespace PharmaBill.Core.Entities;

public sealed class Drug : EntityBase
{
	public string Name { get; set; } = string.Empty;

	public string? GenericName { get; set; }

	public string? BrandName { get; set; }

	public string? Strength { get; set; }

	public string? DosageForm { get; set; }

	public string? Unit { get; set; }

	public string? Barcode { get; set; }

	public string? Schedule { get; set; }

	public string? HsnCode { get; set; }

	/// <summary>Null means not known. 0 counts as known only together with <see cref="IsNilRated"/>.</summary>
	public decimal? GstRate { get; set; }

	/// <summary>Explicitly billed at 0% ("Nil-rated"), as opposed to a rate nobody has entered.</summary>
	public bool IsNilRated { get; set; }

	public decimal? Mrp { get; set; }

	public decimal? SalePrice { get; set; }

	public decimal ReorderLevel { get; set; }

	public decimal StockQuantity { get; set; }

	public Guid? CatalogMedicineId { get; set; }

	public bool IsActive { get; set; } = true;

	public bool IsBanned { get; set; }

	public decimal MaxStockLevel { get; set; }
}
