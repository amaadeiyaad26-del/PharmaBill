using System.Globalization;

namespace PharmaBill.Core.Labels;

public sealed record BarcodeLabelItem(string Name, string? Strength, string? Barcode, decimal? Mrp, string? BatchNo, DateOnly? ExpiryDate, int Copies);

public sealed record BarcodeLabelCell(string Title, string Subtitle, string Price, string Batch, string Expiry, string Code);

public sealed record BarcodeLabelPlan(IReadOnlyList<BarcodeLabelCell> Cells, int SkippedWithoutBarcode);

public static class BarcodeLabelPlanner
{
	public static BarcodeLabelPlan Plan(IEnumerable<BarcodeLabelItem> items)
	{
		ArgumentNullException.ThrowIfNull(items);
		List<BarcodeLabelCell> cells = new List<BarcodeLabelCell>();
		int skipped = 0;
		foreach (BarcodeLabelItem item in items)
		{
			if (item.Copies <= 0)
			{
				continue;
			}
			string? code = NormaliseCode(item.Barcode);
			if (code == null)
			{
				skipped++;
				continue;
			}
			BarcodeLabelCell cell = BuildCell(item, code);
			for (int i = 0; i < item.Copies; i++)
			{
				cells.Add(cell);
			}
		}
		return new BarcodeLabelPlan(cells, skipped);
	}

	public static IReadOnlyList<IReadOnlyList<BarcodeLabelCell>> Paginate(IReadOnlyList<BarcodeLabelCell> cells, int columns, int rowsPerPage)
	{
		if (columns <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(columns));
		}
		if (rowsPerPage <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(rowsPerPage));
		}
		int perPage = columns * rowsPerPage;
		List<IReadOnlyList<BarcodeLabelCell>> pages = new List<IReadOnlyList<BarcodeLabelCell>>();
		for (int start = 0; start < cells.Count; start += perPage)
		{
			pages.Add(cells.Skip(start).Take(perPage).ToList());
		}
		return pages;
	}

	/// <summary>
	/// Returns the barcode with surrounding whitespace removed, or null when it can't be printed.
	/// Code 128 covers printable ASCII, so anything outside that range is rejected.
	/// </summary>
	public static string? NormaliseCode(string? barcode)
	{
		string trimmed = (barcode ?? string.Empty).Trim();
		if (trimmed.Length == 0 || trimmed.Length > 48)
		{
			return null;
		}
		foreach (char c in trimmed)
		{
			if (c < ' ' || c > '~')
			{
				return null;
			}
		}
		return trimmed;
	}

	private static BarcodeLabelCell BuildCell(BarcodeLabelItem item, string code)
	{
		string title = item.Name.Trim();
		string subtitle = string.IsNullOrWhiteSpace(item.Strength) ? string.Empty : item.Strength.Trim();
		string price = item.Mrp is decimal mrp
			? "MRP ₹" + mrp.ToString("0.00", CultureInfo.InvariantCulture)
			: string.Empty;
		string batch = string.IsNullOrWhiteSpace(item.BatchNo) ? string.Empty : "B: " + item.BatchNo.Trim();
		string expiry = item.ExpiryDate is DateOnly exp
			? "Exp " + exp.ToString("MM/yyyy", CultureInfo.InvariantCulture)
			: string.Empty;
		return new BarcodeLabelCell(title, subtitle, price, batch, expiry, code);
	}
}
