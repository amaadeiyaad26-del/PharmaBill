using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>An active product that cannot be billed until its HSN code or GST rate is entered.</summary>
public sealed record ProductMissingTaxRow(Guid DrugId, string Name, string? HsnCode, decimal? GstRate, bool IsNilRated, string Missing);

public sealed record ProductTaxUpdate(Guid DrugId, string? HsnCode, decimal? GstRate, bool IsNilRated);

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
		drug.UpdatedAtUtc = DateTime.UtcNow;
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
