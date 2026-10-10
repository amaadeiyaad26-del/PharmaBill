using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>Partial-name match against the local SQLite drug / catalog master when online AI is busy.</summary>
public static class LocalDrugSuggestionLookup
{
	public static async Task<SmartDrugSuggestion?> FindAsync(PharmaBillDbContext context, string query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);
		string typed = query?.Trim() ?? string.Empty;
		if (typed.Length < 2)
		{
			return null;
		}

		string like = "%" + typed.Replace("%", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal) + "%";

		Drug? drug = await context.Drugs.AsNoTracking()
			.Where(row => row.IsActive && (
				EF.Functions.Like(row.BrandName ?? string.Empty, like)
				|| EF.Functions.Like(row.Name, like)
				|| EF.Functions.Like(row.GenericName ?? string.Empty, like)))
			.OrderBy(row => row.BrandName ?? row.Name)
			.FirstOrDefaultAsync(cancellationToken);

		if (drug != null)
		{
			return ToSuggestion(
				drug.BrandName ?? drug.Name,
				drug.GenericName,
				manufacturer: null,
				packSizeLabel: drug.Unit ?? drug.DosageForm,
				gstRate: drug.GstRate,
				hsnCode: drug.HsnCode,
				mrp: drug.Mrp);
		}

		CatalogMedicine? catalog = await context.CatalogMedicines.AsNoTracking()
			.Where(row => !row.IsDiscontinued && (
				EF.Functions.Like(row.BrandName ?? string.Empty, like)
				|| EF.Functions.Like(row.Name, like)
				|| EF.Functions.Like(row.GenericName ?? string.Empty, like)
				|| EF.Functions.Like(row.ShortComposition1 ?? string.Empty, like)
				|| EF.Functions.Like(row.ShortComposition2 ?? string.Empty, like)))
			.OrderBy(row => row.BrandName ?? row.Name)
			.FirstOrDefaultAsync(cancellationToken);

		if (catalog == null)
		{
			return null;
		}

		string? composition = JoinComposition(catalog.ShortComposition1, catalog.ShortComposition2) ?? catalog.GenericName;
		return ToSuggestion(
			catalog.BrandName ?? catalog.Name,
			composition,
			catalog.Manufacturer,
			catalog.PackSizeLabel,
			gstRate: null,
			hsnCode: null,
			mrp: catalog.ReferencePrice);
	}

	private static SmartDrugSuggestion ToSuggestion(
		string brand,
		string? generic,
		string? manufacturer,
		string? packSizeLabel,
		decimal? gstRate,
		string? hsnCode,
		decimal? mrp)
	{
		decimal gst = gstRate is > 0m and <= 100m ? gstRate.Value : 12m;
		string hsn = string.IsNullOrWhiteSpace(hsnCode) ? "3004" : hsnCode.Trim();
		return new SmartDrugSuggestion(
			brand.Trim(),
			string.IsNullOrWhiteSpace(generic) ? null : generic.Trim(),
			string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer.Trim(),
			string.IsNullOrWhiteSpace(packSizeLabel) ? null : packSizeLabel.Trim(),
			gst,
			hsn,
			mrp is > 0m ? mrp : null);
	}

	private static string? JoinComposition(string? first, string? second)
	{
		string a = first?.Trim() ?? string.Empty;
		string b = second?.Trim() ?? string.Empty;
		if (a.Length == 0)
		{
			return b.Length == 0 ? null : b;
		}

		return b.Length == 0 ? a : a + " + " + b;
	}
}
