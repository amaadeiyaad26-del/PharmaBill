using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>A medicine found in sellable stock: its batches, earliest expiry first (FEFO), and its pack label.</summary>
public sealed record PosStockMatch(Guid DrugId, string DrugName, IReadOnlyList<RetailStockChoice> Batches, string? PackLabel, bool SpellingCorrected);

/// <summary>A medicine that matched but cannot be sold now: in the shop's list without stock, or only in the drug bank.</summary>
public sealed record PosMissingMedicine(Guid? DrugId, Guid? CatalogMedicineId, string Name, string? Composition, string? Manufacturer, string Where);

/// <summary>What a typed medicine name found: sellable stock, or the nearest medicines that are not in stock.</summary>
public sealed record PosMedicineMatch(PosStockMatch? InStock, IReadOnlyList<PosMissingMedicine> NotInStock);

/// <summary>
/// Finds the medicine a counter command names. Sellable stock first, as typed; then the closest spelling among the shop's
/// own medicines ("pacimol" for "Pacimol 650", "paracetmol" for "Paracetamol"); then the drug bank, so a medicine with no
/// stock is named rather than lost. Local only.
/// </summary>
public sealed class PosMedicineMatcher(PharmaBillDbContext context, RetailBillingService billing, CatalogSearchService catalogSearch)
{
	/// <summary>How close a misspelling must be to count as the same medicine.</summary>
	public const double SpellingThreshold = 0.75;

	public async Task<PosMedicineMatch> MatchAsync(string medicineText, CancellationToken cancellationToken = default)
	{
		string typed = medicineText.Trim();
		IReadOnlyList<RetailStockChoice> stock = await billing.SearchStockAsync(typed, cancellationToken);
		if (stock.Count > 0)
		{
			return new PosMedicineMatch(await PickAsync(stock, typed, corrected: false, cancellationToken), []);
		}

		// The shop's own medicines, allowing for a misspelling.
		List<DrugNameCandidate> own = await context.Drugs.AsNoTracking()
			.Where(drug => drug.IsActive)
			.Select(drug => new DrugNameCandidate(drug.Id, drug.Name, drug.BrandName, drug.GenericName))
			.ToListAsync(cancellationToken);
		DrugNameMatch? close = FuzzyMatcher.FindBestMatch(typed, own, SpellingThreshold);
		if (close != null)
		{
			IReadOnlyList<RetailStockChoice> closeStock = (await billing.SearchStockAsync(close.MatchedName, cancellationToken))
				.Where(choice => choice.DrugId == close.DrugId)
				.ToArray();
			if (closeStock.Count > 0)
			{
				return new PosMedicineMatch(await PickAsync(closeStock, close.MatchedName, corrected: true, cancellationToken), []);
			}
		}

		var missing = new List<PosMissingMedicine>();
		if (close != null)
		{
			Drug drug = await context.Drugs.AsNoTracking().FirstAsync(item => item.Id == close.DrugId, cancellationToken);
			missing.Add(new PosMissingMedicine(drug.Id, drug.CatalogMedicineId, drug.Name, drug.GenericName, null, "In your medicine list, no stock"));
		}
		foreach (MedicineSearchResult result in await DrugBankAsync(typed, cancellationToken))
		{
			if (missing.Count >= 5)
			{
				break;
			}
			if (missing.All(item => item.CatalogMedicineId != result.CatalogMedicineId))
			{
				missing.Add(new PosMissingMedicine(result.DrugId, result.CatalogMedicineId, result.Name, result.Composition, result.Manufacturer,
					result.DrugId.HasValue ? "In your medicine list, no stock" : "Drug bank"));
			}
		}
		return new PosMedicineMatch(null, missing);
	}

	// The drug bank as typed; failing that, by the first letters of the first word, ranked by how close the names are.
	private async Task<IReadOnlyList<MedicineSearchResult>> DrugBankAsync(string typed, CancellationToken cancellationToken)
	{
		MedicineSearchResults found = await catalogSearch.SearchAsync(typed, cancellationToken);
		List<MedicineSearchResult> results = found.FromCatalog.Concat(found.InStock).ToList();
		if (results.Count > 0)
		{
			return results;
		}
		string firstWord = typed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
		if (firstWord.Length < 5)
		{
			return results;
		}
		MedicineSearchResults widened = await catalogSearch.SearchAsync(firstWord[..4], cancellationToken);
		return widened.FromCatalog.Concat(widened.InStock)
			.Select(result => (Result: result, Score: FuzzyMatcher.ScoreName(typed, result.Name)))
			.Where(item => item.Score >= 0.6)
			.OrderByDescending(item => item.Score)
			.Select(item => item.Result)
			.ToList();
	}

	private async Task<PosStockMatch> PickAsync(IReadOnlyList<RetailStockChoice> stock, string typed, bool corrected, CancellationToken cancellationToken)
	{
		// Several medicines can contain the typed text ("dolo" → Dolo 650, Dolo 500): the closest name wins.
		IGrouping<Guid, RetailStockChoice> best = stock
			.GroupBy(choice => choice.DrugId)
			.OrderByDescending(group => FuzzyMatcher.ScoreName(typed, group.First().DrugName))
			.First();
		RetailStockChoice[] batches = best.OrderBy(choice => choice.ExpiryDate ?? DateOnly.MaxValue).ThenBy(choice => choice.BatchNo, StringComparer.OrdinalIgnoreCase).ToArray();
		Drug drug = await context.Drugs.AsNoTracking().FirstAsync(item => item.Id == best.Key, cancellationToken);
		string? packLabel = drug.CatalogMedicineId is Guid catalogId
			? await context.CatalogMedicines.AsNoTracking().Where(item => item.Id == catalogId).Select(item => item.PackSizeLabel).FirstOrDefaultAsync(cancellationToken)
			: null;
		return new PosStockMatch(drug.Id, drug.Name, batches, packLabel ?? drug.Unit, corrected);
	}
}
