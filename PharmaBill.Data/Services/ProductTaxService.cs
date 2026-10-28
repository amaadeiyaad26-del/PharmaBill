using PharmaBill.Core;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>An active product that cannot be billed until its HSN code or GST rate is entered.</summary>
public sealed record ProductMissingTaxRow(Guid DrugId, string Name, string? HsnCode, decimal? GstRate, bool IsNilRated, string Missing);

public sealed record ProductTaxUpdate(Guid DrugId, string? HsnCode, decimal? GstRate, bool IsNilRated);

/// <summary>A GST rate the shop recorded, with where and when: offered as "same as last time", never guessed.</summary>
public sealed record RememberedRate(decimal? Rate, bool IsNilRated, DateOnly RecordedOn, string From, DateTime RecordedAtUtc)
{
	public string RateText => IsNilRated ? ProductTax.NilRatedLabel : Rate!.Value.ToString("0.##") + "%";

	/// <summary>Shown beside the rate box, always with the date.</summary>
	public string Label => $"Same as last time: {RateText}, from {From}, {RecordedOn:dd-MMM-yyyy}";
}

/// <summary>
/// The tax that every medicine in the master with the same salt and strength carries. <see cref="Disagree"/> when they
/// carry different ones, and then nothing is suggested.
/// </summary>
public sealed record SameSaltTax(string? HsnCode, decimal? GstRate, bool IsNilRated, string? ExampleName, bool Disagree)
{
	public bool HasSuggestion => !Disagree && HsnCode != null;
}

/// <summary>Reads and records a product's HSN code and GST rate. Every change is audited with the old and new values.</summary>
public sealed class ProductTaxService(PharmaBillDbContext context)
{
	public async Task<IReadOnlyList<ProductMissingTaxRow>> GetProductsMissingTaxAsync(CancellationToken cancellationToken = default)
	{
		// Same rule as ProductTax.IsComplete, written so SQLite can filter it.
		var rows = await context.Drugs.AsNoTracking()
			.Where(drug => drug.IsActive
				&& (drug.HsnCode == null || drug.HsnCode.Trim() == ""
					|| (!drug.IsNilRated && (drug.GstRate == null || drug.GstRate <= 0m))))
			.Select(drug => new { drug.Id, drug.Name, drug.HsnCode, drug.GstRate, drug.IsNilRated })
			.ToListAsync(cancellationToken);
		return rows
			.Select(row => new ProductMissingTaxRow(row.Id, row.Name, row.HsnCode, row.GstRate, row.IsNilRated,
				ProductTax.DescribeMissing(row.HsnCode, row.GstRate, row.IsNilRated) ?? string.Empty))
			.Where(row => row.Missing.Length > 0)
			.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// The drug bank carries no HSN or GST. For a drug bank medicine, the medicines already in the master with the same
	/// salt and strength (same composition key) tell what it is taxed at, but only when they all agree.
	/// </summary>
	public async Task<SameSaltTax> SuggestFromSameSaltAsync(Guid catalogMedicineId, CancellationToken cancellationToken = default)
	{
		string? key = await context.CatalogMedicines.AsNoTracking()
			.Where(medicine => medicine.Id == catalogMedicineId)
			.Select(medicine => medicine.CompositionKey)
			.FirstOrDefaultAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(key))
		{
			return new SameSaltTax(null, null, false, null, false);
		}
		var siblings = await (from drug in context.Drugs.AsNoTracking()
				join medicine in context.CatalogMedicines.AsNoTracking() on drug.CatalogMedicineId equals medicine.Id
				where drug.IsActive && medicine.CompositionKey == key && drug.HsnCode != null && drug.HsnCode.Trim() != ""
					&& (drug.IsNilRated || drug.GstRate > 0m)
				select new { drug.Name, drug.HsnCode, drug.GstRate, drug.IsNilRated })
			.ToListAsync(cancellationToken);
		var rates = siblings
			.Select(row => (Hsn: row.HsnCode!.Trim(), Rate: row.IsNilRated ? null : row.GstRate, row.IsNilRated))
			.Distinct()
			.ToList();
		return rates.Count switch
		{
			0 => new SameSaltTax(null, null, false, null, false),
			1 => new SameSaltTax(rates[0].Hsn, rates[0].Rate, rates[0].IsNilRated, siblings.Select(row => row.Name).Order(StringComparer.OrdinalIgnoreCase).First(), false),
			_ => new SameSaltTax(null, null, false, null, true),
		};
	}

	/// <summary>
	/// The HSN code for a product from what it is: its dosage form, its drug bank pack and composition, or its name's
	/// form words and the short exception lists. A classification only: no rate comes from here.
	/// </summary>
	public async Task<HsnSuggestion?> ClassifyHsnAsync(Guid? drugId, Guid? catalogMedicineId, string? name, CancellationToken cancellationToken = default)
	{
		var drug = drugId is Guid id
			? await context.Drugs.AsNoTracking().Where(item => item.Id == id).Select(item => new { item.Name, item.DosageForm, item.CatalogMedicineId }).FirstOrDefaultAsync(cancellationToken)
			: null;
		Guid? catalogId = catalogMedicineId ?? drug?.CatalogMedicineId;
		var catalog = catalogId is Guid cid
			? await context.CatalogMedicines.AsNoTracking().Where(item => item.Id == cid).Select(item => new { item.Name, item.DosageForm, item.PackSizeLabel, item.ShortComposition1 }).FirstOrDefaultAsync(cancellationToken)
			: null;
		// The same salt and strength already classified in this shop's master (all agreeing) is the most specific answer.
		if (catalogId is Guid salt && await SuggestFromSameSaltAsync(salt, cancellationToken) is { HasSuggestion: true } same)
		{
			return new HsnSuggestion(same.HsnCode!, $"Same HSN as {same.ExampleName}, the same salt in your medicine master");
		}
		string? productName = string.IsNullOrWhiteSpace(name) ? drug?.Name ?? catalog?.Name : name;
		return HsnClassifier.Classify(productName, drug?.DosageForm ?? catalog?.DosageForm, catalog?.PackSizeLabel, catalog?.ShortComposition1);
	}

	/// <summary>
	/// "Same as last time": the most recent GST rate this shop recorded for the medicine, or for another medicine with the
	/// same salt and strength, from a supplier's invoice line or the product record. Only the single most recent record
	/// counts; null when it was recorded before the September 2025 rate cut, is not a valid slab, or there is none.
	/// Never read from a medicine's name.
	/// </summary>
	public async Task<RememberedRate?> LastRecordedRateAsync(Guid? drugId, Guid? catalogMedicineId, CancellationToken cancellationToken = default)
	{
		Guid? resolvedDrugId = drugId ?? (catalogMedicineId is Guid catalogLookup
			? await context.Drugs.AsNoTracking().Where(item => item.CatalogMedicineId == catalogLookup && item.IsActive).OrderBy(item => item.CreatedAtUtc).Select(item => (Guid?)item.Id).FirstOrDefaultAsync(cancellationToken)
			: null);
		Guid? catalogId = catalogMedicineId ?? (resolvedDrugId is Guid lookup
			? await context.Drugs.AsNoTracking().Where(item => item.Id == lookup).Select(item => item.CatalogMedicineId).FirstOrDefaultAsync(cancellationToken)
			: null);
		string? compositionKey = catalogId is Guid keyLookup
			? await context.CatalogMedicines.AsNoTracking().Where(item => item.Id == keyLookup).Select(item => item.CompositionKey).FirstOrDefaultAsync(cancellationToken)
			: null;

		// This medicine, and every medicine in the master with exactly the same salt and strength.
		List<Guid> candidates = resolvedDrugId is Guid own ? [own] : [];
		if (!string.IsNullOrWhiteSpace(compositionKey))
		{
			candidates.AddRange(await (from drug in context.Drugs.AsNoTracking()
					join medicine in context.CatalogMedicines.AsNoTracking() on drug.CatalogMedicineId equals medicine.Id
					where drug.IsActive && medicine.CompositionKey == compositionKey
					select drug.Id)
				.ToListAsync(cancellationToken));
		}
		candidates = candidates.Distinct().ToList();
		if (candidates.Count == 0)
		{
			return null;
		}

		var lines = await (from item in context.PurchaseItems.AsNoTracking()
				join invoice in context.PurchaseInvoices.AsNoTracking() on item.PurchaseInvoiceId equals invoice.Id
				join drug in context.Drugs.AsNoTracking() on item.DrugId equals drug.Id
				where candidates.Contains(item.DrugId) && !item.IsDeleted && !invoice.IsDeleted && item.TaxRate > 0m
				select new { item.DrugId, drug.Name, item.TaxRate, invoice.InvoiceDate, invoice.InvoiceNo, item.CreatedAtUtc })
			.ToListAsync(cancellationToken);
		var masters = await context.Drugs.AsNoTracking()
			.Where(drug => candidates.Contains(drug.Id) && (drug.IsNilRated || drug.GstRate > 0m))
			.Select(drug => new { drug.Id, drug.Name, drug.GstRate, drug.IsNilRated, drug.CreatedAtUtc })
			.ToListAsync(cancellationToken);
		var taxSetAt = await context.AuditLogs.AsNoTracking()
			.Where(log => log.Action == "ProductTaxDetailsSet" && log.EntityId != null && candidates.Contains(log.EntityId.Value))
			.GroupBy(log => log.EntityId!.Value)
			.Select(group => new { DrugId = group.Key, At = group.Max(log => log.ActionAtUtc) })
			.ToDictionaryAsync(row => row.DrugId, row => row.At, cancellationToken);

		var records = lines
			.Select(line => new RememberedRate(line.TaxRate, false, line.InvoiceDate,
				"supplier invoice " + line.InvoiceNo + (line.DrugId == resolvedDrugId ? string.Empty : " for " + line.Name + " (same salt)"), line.CreatedAtUtc))
			.Concat(masters.Select(drug =>
			{
				DateTime at = taxSetAt.TryGetValue(drug.Id, out DateTime set) && set > drug.CreatedAtUtc ? set : drug.CreatedAtUtc;
				return new RememberedRate(drug.IsNilRated ? null : drug.GstRate, drug.IsNilRated, DateOnly.FromDateTime(at),
					drug.Id == resolvedDrugId ? "this medicine's record" : drug.Name + " (same salt)", at);
			}))
			.OrderByDescending(record => record.RecordedOn)
			.ThenByDescending(record => record.RecordedAtUtc)
			.ToList();
		RememberedRate? latest = records.FirstOrDefault();
		if (latest == null || GstRates.IsBeforeRateCut(latest.RecordedOn) || (!latest.IsNilRated && !GstRates.IsStandardSlab(latest.Rate!.Value)))
		{
			return null;
		}
		return latest;
	}

	/// <summary>Validates and saves one product's tax details. Throws <see cref="InvalidOperationException"/> naming the product when the entry is invalid.</summary>
	public async Task<ProductTaxDetails> SetAsync(ProductTaxUpdate update, Guid? userId, string source, CancellationToken cancellationToken = default)
	{
		ProductTaxDetails details = Apply(await LoadAsync(update.DrugId, cancellationToken), update, userId, source);
		await context.SaveChangesAsync(cancellationToken);
		return details;
	}

	/// <summary>Saves several products in one transaction. Nothing is saved if any entry is invalid.</summary>
	public async Task<int> SetManyAsync(IReadOnlyCollection<ProductTaxUpdate> updates, Guid? userId, string source, CancellationToken cancellationToken = default)
	{
		if (updates.Count == 0)
		{
			return 0;
		}

		Guid[] ids = updates.Select(update => update.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await context.Drugs.Where(drug => ids.Contains(drug.Id)).ToDictionaryAsync(drug => drug.Id, cancellationToken);
		foreach (ProductTaxUpdate update in updates)
		{
			if (!drugs.TryGetValue(update.DrugId, out Drug? drug))
			{
				throw new InvalidOperationException("A product in the list no longer exists. Refresh and try again.");
			}

			Apply(drug, update, userId, source);
		}

		await context.SaveChangesAsync(cancellationToken);
		return updates.Count;
	}

	private async Task<Drug> LoadAsync(Guid drugId, CancellationToken cancellationToken) =>
		await context.Drugs.FirstOrDefaultAsync(drug => drug.Id == drugId, cancellationToken)
			?? throw new InvalidOperationException("The product no longer exists.");

	private ProductTaxDetails Apply(Drug drug, ProductTaxUpdate update, Guid? userId, string source)
	{
		ProductTaxDetails details = ProductTax.Require(drug.Name, update.HsnCode, update.GstRate, update.IsNilRated);
		string before = Describe(drug.HsnCode, drug.GstRate, drug.IsNilRated);
		drug.HsnCode = details.HsnCode;
		drug.GstRate = details.GstRate;
		drug.IsNilRated = details.IsNilRated;
		drug.UpdatedAtUtc = BusinessClock.UtcNow;
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			Action = "ProductTaxDetailsSet",
			EntityName = "Drug",
			EntityId = drug.Id,
			Details = $"{source}: {drug.Name}: {before} -> {Describe(details.HsnCode, details.GstRate, details.IsNilRated)}"
		});
		return details;
	}

	private static string Describe(string? hsn, decimal? rate, bool nil) =>
		$"HSN {(string.IsNullOrWhiteSpace(hsn) ? "(none)" : hsn.Trim())}, GST {(nil ? ProductTax.NilRatedLabel : rate.HasValue ? rate.Value.ToString("0.##") + "%" : "(none)")}";
}
