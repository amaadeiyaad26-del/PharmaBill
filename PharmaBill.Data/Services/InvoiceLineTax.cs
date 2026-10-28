using System.Globalization;
using System.Text.RegularExpressions;
using PharmaBill.Core.Gst;

namespace PharmaBill.Data.Services;

/// <summary>
/// The HSN code and GST rate printed on each line of a supplier's invoice, read from that line. An invoice-wide rate is
/// used only when the whole invoice declares a single slab: on a mixed-rate invoice a line whose own rate cannot be
/// read is left at 0 (unknown) for the user to fill, never given another line's rate.
/// </summary>
public static partial class InvoiceLineTax
{
	// Chapters a chemist's goods fall in: medicaments, cosmetics, soaps, food supplements, rubber goods, instruments…
	private static readonly HashSet<string> Chapters = ["12", "13", "17", "19", "21", "22", "28", "29", "30", "33", "34", "38", "39", "40", "48", "56", "63", "90", "94", "96"];

	/// <summary>Digits only, 4, 6 or 8 long, in a chapter a pharmacy sells from; null otherwise.</summary>
	public static string? NormalizeHsn(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}
		string digits = new(raw.Where(char.IsDigit).ToArray());
		return digits.Length is 4 or 6 or 8 && Chapters.Contains(digits[..2]) ? digits : null;
	}

	/// <summary>
	/// The line's total GST from what was printed: a real slab stays as it is; a CGST or SGST half (2.5, 6, 9, 14)
	/// becomes the slab it is half of. Anything else is returned unchanged (and is not accepted as a rate later).
	/// </summary>
	public static decimal TotalRate(decimal printed)
	{
		if (printed <= 0m || GstRates.IsStandardSlab(printed))
		{
			return printed;
		}
		return GstRates.IsStandardSlab(printed * 2m) ? printed * 2m : printed;
	}

	/// <summary>The distinct total GST slabs the invoice declares anywhere (GST @ 12, IGST 18%, CGST @ 2.5 + SGST @ 2.5…).</summary>
	public static IReadOnlyList<decimal> InvoiceSlabs(string? text)
	{
		var slabs = new HashSet<decimal>();
		foreach (Match match in DeclaredRate().Matches(text ?? string.Empty))
		{
			if (!decimal.TryParse(match.Groups["rate"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate))
			{
				continue;
			}
			bool half = match.Groups["kind"].Value.StartsWith('C') || match.Groups["kind"].Value.StartsWith('S') || match.Groups["kind"].Value.StartsWith('c') || match.Groups["kind"].Value.StartsWith('s');
			decimal total = half ? rate * 2m : rate;
			if (GstRates.IsStandardSlab(total))
			{
				slabs.Add(total);
			}
		}
		return slabs.OrderBy(rate => rate).ToList();
	}

	/// <summary>The HSN and GST printed on one invoice row; either is null when the row does not show it.</summary>
	public static (string? Hsn, decimal? Rate) ReadRow(string row)
	{
		string? hsn = null;
		Match hsnMatch = Match.Empty;
		foreach (Match candidate in HsnToken().Matches(row))
		{
			if (NormalizeHsn(candidate.Value) is string code)
			{
				hsn = code;
				hsnMatch = candidate;
				break;
			}
		}

		// Percentages on the row: one slab ("12%"), or the CGST and SGST halves ("6% 6%", "2.5% 2.5%").
		List<decimal> percents = Percent().Matches(row)
			.Select(match => decimal.TryParse(match.Groups["rate"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : -1m)
			.Where(value => value > 0m)
			.ToList();
		decimal? rate = FromNumbers(percents);

		// No percent sign: a rate printed right after the HSN code ("30049099 12", "3004 2.5 2.5").
		if (rate == null && hsnMatch.Success)
		{
			List<decimal> following = AfterHsn().Match(row[(hsnMatch.Index + hsnMatch.Length)..]).Groups["n"].Captures
				.Select(capture => decimal.TryParse(capture.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : -1m)
				.ToList();
			if (following.Count > 0 && GstRates.IsStandardSlab(following[0]))
			{
				rate = following[0];
			}
			else if (following.Count > 1 && following[0] == following[1] && GstRates.IsStandardSlab(following[0] * 2m))
			{
				rate = following[0] * 2m;
			}
		}
		return (hsn, rate);
	}

	private static decimal? FromNumbers(IReadOnlyList<decimal> values)
	{
		List<decimal> slabs = values.Where(GstRates.IsStandardSlab).Distinct().ToList();
		for (int i = 0; i + 1 < values.Count; i++)
		{
			if (values[i] == values[i + 1] && GstRates.IsStandardSlab(values[i] * 2m) && !GstRates.IsStandardSlab(values[i]))
			{
				return values[i] * 2m;
			}
		}
		return slabs.Count == 1 ? slabs[0] : null;
	}

	/// <summary>
	/// Sets each line's GST and HSN from its own row. A line whose rate is not on its row takes the invoice's rate only
	/// when the invoice declares exactly one slab; otherwise 0, so the user is asked rather than given a wrong rate.
	/// </summary>
	public static List<ExtractedPurchaseLine> Attach(IReadOnlyList<ExtractedPurchaseLine> lines, IReadOnlyList<string> rows, string? invoiceText)
	{
		IReadOnlyList<decimal> slabs = InvoiceSlabs(invoiceText);
		decimal fallback = slabs.Count == 1 ? slabs[0] : 0m;
		var result = new List<ExtractedPurchaseLine>(lines.Count);
		foreach (ExtractedPurchaseLine line in lines)
		{
			string? row = FindRow(line, rows);
			(string? hsn, decimal? rate) = row == null ? (null, null) : ReadRow(row);
			result.Add(line with { Gst = rate ?? fallback, HsnCode = hsn ?? line.HsnCode });
		}
		return result;
	}

	// The row that carries this line: the one with its batch code, else the one starting with its name.
	private static string? FindRow(ExtractedPurchaseLine line, IReadOnlyList<string> rows)
	{
		string batch = line.Batch?.Trim() ?? string.Empty;
		if (batch.Length >= 4 && !string.Equals(batch, "NA", StringComparison.OrdinalIgnoreCase))
		{
			string? byBatch = rows.FirstOrDefault(row => row.Contains(batch, StringComparison.OrdinalIgnoreCase));
			if (byBatch != null)
			{
				return byBatch;
			}
		}
		string[] words = line.ItemName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0)
		{
			return null;
		}
		string head = string.Join(' ', words.Take(Math.Min(2, words.Length)));
		return rows.FirstOrDefault(row => row.Contains(head, StringComparison.OrdinalIgnoreCase));
	}

	[GeneratedRegex(@"(?<kind>CGST|SGST|IGST|UTGST|GST)\s*(?:@|:|-)?\s*(?<rate>\d{1,2}(?:\.\d{1,2})?)\s*%?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex DeclaredRate();

	[GeneratedRegex(@"(?<![\d./-])\d{4}(?:\d{2}){0,2}(?![\d./-])", RegexOptions.CultureInvariant)]
	private static partial Regex HsnToken();

	[GeneratedRegex(@"(?<![\d.])(?<rate>\d{1,2}(?:\.\d{1,2})?)\s*%", RegexOptions.CultureInvariant)]
	private static partial Regex Percent();

	[GeneratedRegex(@"^\s*(?:(?<n>\d{1,2}(?:\.\d{1,2})?)(?![\d.])\s*){1,2}", RegexOptions.CultureInvariant)]
	private static partial Regex AfterHsn();
}
