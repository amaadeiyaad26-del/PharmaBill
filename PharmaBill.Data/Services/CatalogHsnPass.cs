using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>What the one-time HSN pass did: filled 3004, left an exception for the user, or could not classify.</summary>
public sealed record CatalogHsnPassResult(bool AlreadyRun, int Examined, int Filled, int ExceptionsLeft, int Unclassified)
{
	public string Summary => AlreadyRun
		? "The HSN pass has already run on this database."
		: $"HSN pass: {Examined} products had no HSN; {Filled} set to 3004 from their dosage form, {ExceptionsLeft} look like an exception (left for you), {Unclassified} could not be classified.";
}

/// <summary>
/// Once per database: products with no HSN whose dosage form clearly makes them a medicament get 3004, so the HSN
/// dialog stops appearing for them. Nothing else is filled (an exception such as a soap or a syringe is left for the
/// user to confirm), no rate is touched, and every product changed is audited with the reason.
/// </summary>
public sealed class CatalogHsnPass(PharmaBillDbContext context)
{
	public const string AuditAction = "CatalogHsnPass";

	public async Task<CatalogHsnPassResult> RunOnceAsync(Guid? userId = null, CancellationToken cancellationToken = default)
	{
		if (await context.AuditLogs.AsNoTracking().AnyAsync(log => log.Action == AuditAction, cancellationToken))
		{
			return new CatalogHsnPassResult(true, 0, 0, 0, 0);
		}

		var rows = await (from drug in context.Drugs.AsNoTracking()
				where drug.IsActive && (drug.HsnCode == null || drug.HsnCode.Trim() == "")
				join medicine in context.CatalogMedicines.AsNoTracking() on drug.CatalogMedicineId equals medicine.Id into catalog
				from medicine in catalog.DefaultIfEmpty()
				select new { drug.Id, Pack = medicine == null ? null : medicine.PackSizeLabel, Composition = medicine == null ? null : medicine.ShortComposition1 })
			.ToListAsync(cancellationToken);
		Guid[] ids = rows.Select(row => row.Id).ToArray();
		Dictionary<Guid, Drug> drugs = await context.Drugs.Where(drug => ids.Contains(drug.Id)).ToDictionaryAsync(drug => drug.Id, cancellationToken);
		var products = rows.Select(row => new { Drug = drugs[row.Id], row.Pack, row.Composition }).ToList();

		int filled = 0, exceptions = 0, unclassified = 0;
		DateTime now = BusinessClock.UtcNow;
		foreach (var product in products)
		{
			HsnSuggestion? suggestion = HsnClassifier.Classify(product.Drug.Name, product.Drug.DosageForm, product.Pack, product.Composition);
			if (suggestion == null)
			{
				unclassified++;
				continue;
			}
			if (!suggestion.IsMedicament)
			{
				exceptions++;
				continue;
			}
			product.Drug.HsnCode = HsnClassifier.Medicament;
			product.Drug.UpdatedAtUtc = now;
			context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				ActionAtUtc = now,
				Action = "ProductHsnClassified",
				EntityName = "Drug",
				EntityId = product.Drug.Id,
				Details = $"{product.Drug.Name}: HSN (none) -> 3004 by the one-time catalogue pass. {suggestion.Reason}. GST rate not changed."
			});
			filled++;
		}

		var result = new CatalogHsnPassResult(false, products.Count, filled, exceptions, unclassified);
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			ActionAtUtc = now,
			Action = AuditAction,
			EntityName = "Drug",
			Details = result.Summary
		});
		await context.SaveChangesAsync(cancellationToken);
		return result;
	}
}
