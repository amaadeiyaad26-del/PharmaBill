namespace PharmaBill.Core.Entities;

public sealed class CatalogMedicine : EntityBase
{
	public string Name { get; set; } = string.Empty;

	public string? GenericName { get; set; }

	public string? BrandName { get; set; }

	public string? Strength { get; set; }

	public string? DosageForm { get; set; }

	public string? Manufacturer { get; set; }

	public string? SourceId { get; set; }

	public decimal? ReferencePrice { get; set; }

	public bool IsDiscontinued { get; set; }

	public string? Type { get; set; }

	public string? PackSizeLabel { get; set; }

	public string? ShortComposition1 { get; set; }

	public string? ShortComposition2 { get; set; }

	public string CompositionKey { get; set; } = string.Empty;
}
