using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed class MedicineSearchResults(IReadOnlyList<MedicineSearchResult> inStock, IReadOnlyList<MedicineSearchResult> fromCatalog)
{
	public IReadOnlyList<MedicineSearchResult> InStock { get; } = inStock;

	public IReadOnlyList<MedicineSearchResult> FromCatalog { get; } = fromCatalog;
}
