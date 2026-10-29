using System.Globalization;
using System.Text.RegularExpressions;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>
/// Reads a bill's item table by its columns. The table's own heading row (Product, Pack, HSN, Batch, Exp, Qty, Free,
/// Rate, Amount, GST%...) gives where each column is; every word on an item row is then put in the column it sits
/// under. A row is never read as one run of text to be split by guessing, so a rate cannot land in Amount, a free
/// quantity cannot become a price, and the name holds only what is printed under Product.
/// </summary>
public static partial class InvoiceTextParser
{
	private enum Column { Serial, Product, Manufacturer, Pack, Hsn, Batch, Expiry, Qty, Free, Mrp, Rate, Discount, Amount, Gst, Cgst, Sgst, Igst, Other }

	private sealed record HeaderColumn(Column Kind, double Left, double Right, string Heading);

	private sealed record WordLine(double Y, double Height, List<OcrWordPosition> Words)
	{
		public string Text => string.Join(' ', Words.OrderBy(word => word.X).Select(word => word.Text.Trim()));
	}

	// Columns whose values start at the heading's left edge (text), as against numbers set under or right of it.
	private static readonly HashSet<Column> LeftAligned = [Column.Serial, Column.Product, Column.Manufacturer, Column.Pack, Column.Hsn, Column.Batch, Column.Expiry];

	private static readonly Regex TableEnd = new(
		@"^\s*(tax\s*summary|sub\s*-?\s*total|total\b|taxable|grand\s*total|amount\s+in\s+words|rupees\b|c\.?\s?gst|s\.?\s?gst|ut\.?\s?gst|i\.?\s?gst|round(ing)?\b|net\s*(amount|payable|total)|less\b|e\.?\s*&\s*o)",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex QtyPlusFree = new(@"^\s*(\d+(?:\.\d+)?)\s*\+\s*(\d+(?:\.\d+)?)\s*$", RegexOptions.Compiled);

	/// <summary>
	/// The bill read column by column from positioned words (Y grows down the page). Null when no item-table heading
	/// row is found; the caller then says so rather than guessing.
	/// </summary>
	public static ExtractedPurchaseInvoice? ReadTable(IReadOnlyList<OcrWordPosition> words, string fallbackName)
	{
		ArgumentNullException.ThrowIfNull(words);
		List<WordLine> lines = GroupLines(words.Where(word => !string.IsNullOrWhiteSpace(word.Text)));
		if (lines.Count == 0)
		{
			return null;
		}

		List<ExtractedPurchaseLine> items = new List<ExtractedPurchaseLine>();
		List<decimal> discounts = new List<decimal>();
		bool foundHeader = false;
		int index = 0;
		while (index < lines.Count)
		{
			List<HeaderColumn>? columns = ReadHeading(lines[index]);
			if (columns == null)
			{
				index++;
				continue;
			}

			foundHeader = true;
			index++;
			double[] bounds = Boundaries(columns);
			ExtractedPurchaseLine? current = null;
			double currentHeight = 0;
			for (; index < lines.Count; index++)
			{
				WordLine line = lines[index];
				if (TableEnd.IsMatch(line.Text) || ReadHeading(line) != null)
				{
					break;
				}

				Dictionary<Column, string> cells = Cells(line, columns, bounds);
				if (IsItemRow(cells))
				{
					if (current != null)
					{
						items.Add(current);
					}
					(current, decimal discount) = ToLine(cells);
					discounts.Add(discount);
					currentHeight = line.Height;
					continue;
				}

				// A line with text under Product only: the manufacturer printed small beneath the name, or the name
				// running onto a second line.
				if (current != null && cells.Keys.All(kind => kind is Column.Product or Column.Manufacturer or Column.Serial)
					&& cells.TryGetValue(Column.Product, out string? more))
				{
					bool smaller = line.Height < currentHeight * 0.92;
					current = smaller || columns.Any(column => column.Kind == Column.Manufacturer)
						? (smaller && string.IsNullOrWhiteSpace(current.Manufacturer) ? current with { Manufacturer = more.Trim() } : current)
						: current with { ItemName = CleanName(current.ItemName + " " + more) };
				}
			}

			if (current != null)
			{
				items.Add(current);
			}
		}

		if (!foundHeader || items.Count == 0)
		{
			return null;
		}

		string[] text = lines.Select(line => line.Text).ToArray();
		InvoicePrintedTotals printed = FindPrintedTotals(text);
		decimal sum = items.Sum(item => item.Amount);
		decimal tax = items.Sum(item => decimal.Round(item.Amount * item.Gst / 100m, 2, MidpointRounding.AwayFromZero));
		ExtractedPurchaseInvoice invoice = new ExtractedPurchaseInvoice(
			"PURCHASE_INVOICE",
			GuessSupplier(text) ?? "Supplier",
			MatchInvoiceNo(text) ?? "UNKNOWN",
			GuessDate(text),
			items,
			sum,
			printed.GrandTotal ?? sum + tax)
		{
			Printed = printed
		};
		InvoiceTextParserCache.Discounts.Add(invoice, discounts);
		return invoice;
	}

	private static List<WordLine> GroupLines(IEnumerable<OcrWordPosition> words)
	{
		List<WordLine> lines = new List<WordLine>();
		foreach (OcrWordPosition word in words.OrderBy(word => word.Y + word.Height / 2).ThenBy(word => word.X))
		{
			double centre = word.Y + word.Height / 2;
			WordLine? last = lines.Count == 0 ? null : lines[^1];
			if (last != null && Math.Abs(centre - last.Y) <= Math.Max(1.5, 0.5 * Math.Min(last.Height, word.Height)))
			{
				last.Words.Add(word);
				continue;
			}
			lines.Add(new WordLine(centre, word.Height, [word]));
		}
		// A line's height is its typical word's, so a small-print line beneath an item can be told apart.
		return lines.Select(line => line with { Height = Median(line.Words.Select(word => word.Height)) }).ToList();
	}

	private static double Median(IEnumerable<double> values)
	{
		double[] sorted = values.OrderBy(value => value).ToArray();
		return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
	}

	/// <summary>The columns of an item-table heading row; null when this line is not one.</summary>
	private static List<HeaderColumn>? ReadHeading(WordLine line)
	{
		List<HeaderColumn> columns = new List<HeaderColumn>();
		List<OcrWordPosition> ordered = line.Words.OrderBy(word => word.X).ToList();
		for (int index = 0; index < ordered.Count; index++)
		{
			OcrWordPosition word = ordered[index];
			string token = Regex.Replace(word.Text.Trim().ToLowerInvariant(), @"[.:]+$", string.Empty);
			string? next = index + 1 < ordered.Count ? ordered[index + 1].Text.Trim().ToLowerInvariant() : null;
			HeaderColumn? previous = columns.Count == 0 ? null : columns[^1];
			double right = word.X + word.Width;

			// Words that belong to the heading before them: "Batch No", "GST %", "Product Name", "Exp. Date".
			if (previous != null && IsHeadingSuffix(token, previous.Kind))
			{
				columns[^1] = previous with { Right = right, Heading = previous.Heading + " " + token };
				continue;
			}

			Column kind = Classify(token, next);
			if (kind == Column.Amount && columns.Any(column => column.Kind == Column.Amount))
			{
				// A second money column (net, total): the first, the taxable amount, is the line's amount.
				kind = token.StartsWith("taxable", StringComparison.Ordinal) ? Column.Amount : Column.Other;
				if (kind == Column.Amount)
				{
					int first = columns.FindIndex(column => column.Kind == Column.Amount);
					columns[first] = columns[first] with { Kind = Column.Other };
				}
			}
			columns.Add(new HeaderColumn(kind, word.X, right, token));
		}

		HashSet<Column> kinds = columns.Select(column => column.Kind).Where(kind => kind != Column.Other).ToHashSet();
		bool isHeading = kinds.Contains(Column.Product) && kinds.Contains(Column.Qty) && kinds.Contains(Column.Amount) && kinds.Count >= 4
			&& columns.Count(column => column.Kind == Column.Other) <= kinds.Count;
		return isHeading ? columns : null;
	}

	private static bool IsHeadingSuffix(string token, Column previous) => token switch
	{
		"no" or "number" or "code" => previous is Column.Batch or Column.Hsn or Column.Serial,
		"%" or "rate" or "(%)" => previous is Column.Gst or Column.Cgst or Column.Sgst or Column.Igst or Column.Discount,
		"name" or "description" => previous is Column.Product,
		"date" or "dt" => previous is Column.Expiry,
		"size" => previous is Column.Pack,
		"value" or "amt" or "amount" => previous is Column.Amount,
		"qty" => previous is Column.Free,
		_ => false
	};

	private static Column Classify(string token, string? next)
	{
		token = token.Replace("₹", string.Empty).Trim('(', ')');
		return token switch
		{
			"#" or "s.no" or "sno" or "sr" or "sr.no" or "srno" or "sl" or "sl.no" or "s/n" or "no" => Column.Serial,
			"product" or "products" or "item" or "items" or "description" or "particulars" or "medicine" or "name" or "item name" => Column.Product,
			"mfr" or "mfgr" or "company" or "manufacturer" or "mkt" or "make" or "mfg.co" => Column.Manufacturer,
			"mfg" => next != null && next.StartsWith("exp", StringComparison.Ordinal) ? Column.Expiry : Column.Manufacturer,
			"pack" or "packing" or "pkg" or "pck" => Column.Pack,
			"hsn" or "hsn/sac" or "hsncode" or "sac" or "hsn code" => Column.Hsn,
			"batch" or "batchno" or "b.no" or "bno" or "batch.no" => Column.Batch,
			"exp" or "expiry" or "exp.dt" or "expdt" or "expdate" or "mfg/exp" or "exp.date" => Column.Expiry,
			"qty" or "quantity" or "qnty" or "qty." => Column.Qty,
			"free" or "sch" or "scheme" or "bonus" or "fr" or "fqty" => Column.Free,
			"mrp" => Column.Mrp,
			"rate" or "ptr" or "pts" or "price" or "rate/unit" => Column.Rate,
			"disc" or "disc%" or "discount" or "dis" or "dis%" or "cd" => Column.Discount,
			"amount" or "amt" or "value" or "total" or "taxable" or "net" or "net amt" => Column.Amount,
			"gst" or "gst%" or "tax" or "tax%" or "gst%." => Column.Gst,
			"cgst" or "cgst%" => Column.Cgst,
			"sgst" or "sgst%" or "utgst" or "utgst%" => Column.Sgst,
			"igst" or "igst%" => Column.Igst,
			_ => Column.Other
		};
	}

	/// <summary>Where each column ends: at the next heading's left edge for text, half way across the gap for numbers.</summary>
	private static double[] Boundaries(List<HeaderColumn> columns)
	{
		double[] bounds = new double[columns.Count];
		for (int index = 0; index < columns.Count; index++)
		{
			if (index == columns.Count - 1)
			{
				bounds[index] = double.MaxValue;
				continue;
			}
			HeaderColumn here = columns[index];
			HeaderColumn next = columns[index + 1];
			bounds[index] = LeftAligned.Contains(next.Kind) || here.Kind is Column.Product or Column.Manufacturer
				? next.Left - 1
				: (here.Right + next.Left) / 2;
		}
		return bounds;
	}

	private static Dictionary<Column, string> Cells(WordLine line, List<HeaderColumn> columns, double[] bounds)
	{
		Dictionary<Column, List<string>> cells = new Dictionary<Column, List<string>>();
		foreach (OcrWordPosition word in line.Words.OrderBy(word => word.X))
		{
			double centre = word.X + word.Width / 2;
			int column = 0;
			while (column < bounds.Length - 1 && centre >= bounds[column])
			{
				column++;
			}
			Column kind = columns[column].Kind;
			if (!cells.TryGetValue(kind, out List<string>? list))
			{
				cells[kind] = list = new List<string>();
			}
			list.Add(word.Text.Trim());
		}
		return cells.ToDictionary(pair => pair.Key, pair => string.Join(' ', pair.Value));
	}

	private static bool IsItemRow(Dictionary<Column, string> cells) =>
		Number(cells, Column.Amount) is > 0m
		|| (Number(cells, Column.Qty) is > 0m && Number(cells, Column.Rate) is > 0m);

	private static (ExtractedPurchaseLine Line, decimal Discount) ToLine(Dictionary<Column, string> cells)
	{
		decimal quantity = Number(cells, Column.Qty) ?? 0m;
		decimal free = Number(cells, Column.Free) ?? 0m;
		if (cells.TryGetValue(Column.Qty, out string? qtyText) && QtyPlusFree.Match(qtyText) is { Success: true } scheme)
		{
			quantity = decimal.Parse(scheme.Groups[1].Value, CultureInfo.InvariantCulture);
			free += decimal.Parse(scheme.Groups[2].Value, CultureInfo.InvariantCulture);
		}

		decimal gst = Number(cells, Column.Gst)
			?? (Number(cells, Column.Igst) is decimal igst ? igst : (Number(cells, Column.Cgst) ?? 0m) + (Number(cells, Column.Sgst) ?? 0m));
		string batch = cells.TryGetValue(Column.Batch, out string? batchText) ? Regex.Replace(batchText, @"\s+", string.Empty) : string.Empty;
		string expiry = cells.TryGetValue(Column.Expiry, out string? expiryText) ? ExtractExpiry(expiryText) : string.Empty;
		ExtractedPurchaseLine line = new ExtractedPurchaseLine(
			CleanName(cells.GetValueOrDefault(Column.Product) ?? string.Empty),
			batch.Length == 0 || batch.Trim('-', '–', '—').Length == 0 ? "NA" : batch,
			expiry,
			quantity,
			free,
			Number(cells, Column.Mrp) ?? 0m,
			Number(cells, Column.Rate) ?? 0m,
			gst,
			Number(cells, Column.Amount) ?? 0m)
		{
			HsnCode = InvoiceLineTax.NormalizeHsn(cells.GetValueOrDefault(Column.Hsn)),
			Manufacturer = Blank(cells.GetValueOrDefault(Column.Manufacturer)),
			Pack = Blank(cells.GetValueOrDefault(Column.Pack))
		};
		return (line, Number(cells, Column.Discount) ?? 0m);
	}

	private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) || text.Trim('-', '–', '—', ' ').Length == 0 ? null : text.Trim();

	/// <summary>The cell as a number; null when empty or a dash (nothing printed).</summary>
	private static decimal? Number(Dictionary<Column, string> cells, Column column)
	{
		if (!cells.TryGetValue(column, out string? text))
		{
			return null;
		}
		string cleaned = Regex.Replace(text, @"(₹|rs\.?|%|,|\s)", string.Empty, RegexOptions.IgnoreCase);
		return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) ? value : null;
	}

	/// <summary>The name as printed under Product, without schedule tags such as "(Sch. H1)".</summary>
	private static string CleanName(string name)
	{
		string cleaned = Regex.Replace(name, @"\(\s*(sch(edule)?\.?\s*)?(h1|h|x|g)\s*\)", " ", RegexOptions.IgnoreCase);
		return Regex.Replace(cleaned, @"\s+", " ").Trim();
	}

	private static readonly Regex PrintedMoney = new(@"(?<![\d.])(\(?-\)?\s*)?(\d{1,3}(?:,\d{2,3})+(?:\.\d+)?|\d+(?:\.\d+)?)(?!\s*%)(?![\d.,])", RegexOptions.Compiled);

	private static readonly (string Label, Regex Pattern)[] TotalLabels =
	[
		("taxable", new Regex(@"^\s*(total\s+)?taxable\s*(value|amount|amt)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
		("subtotal", new Regex(@"^\s*sub\s*-?\s*total\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
		("cgst", new Regex(@"^\s*(total\s+)?c\.?\s?gst\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
		("sgst", new Regex(@"^\s*(total\s+)?(s|ut)\.?\s?gst\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
		("igst", new Regex(@"^\s*(total\s+)?i\.?\s?gst\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
		("round", new Regex(@"^\s*round(ing)?\s*(off)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
		("grand", new Regex(@"^\s*(grand\s*total|net\s*(amount|payable|total)|invoice\s*(total|amount|value)|bill\s*amount|total\s*amount|amount\s*payable)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
	];

	/// <summary>
	/// The totals printed at the foot of the bill, each from a line that starts with its label ("Taxable Value 11,450.00",
	/// "CGST 522.50", "Grand Total Rs 12,495.00"), or from the line below a label printed alone. Percentages are not amounts.
	/// </summary>
	public static InvoicePrintedTotals FindPrintedTotals(IReadOnlyList<string> lines)
	{
		Dictionary<string, decimal> found = new Dictionary<string, decimal>();
		for (int index = 0; index < lines.Count; index++)
		{
			foreach ((string label, Regex pattern) in TotalLabels)
			{
				Match match = pattern.Match(lines[index]);
				if (!match.Success)
				{
					continue;
				}
				decimal? value = LastAmount(lines[index][match.Length..]);
				if (value == null && index + 1 < lines.Count && !TotalLabels.Any(other => other.Pattern.IsMatch(lines[index + 1])))
				{
					string below = lines[index + 1].Trim();
					if (Regex.IsMatch(below, @"^(rs\.?|₹)?\s*[-\d,().\s]+$", RegexOptions.IgnoreCase))
					{
						value = LastAmount(below);
					}
				}
				if (value != null)
				{
					// The last one printed is the bill's own summary, below any per-rate breakdown.
					found[label] = value.Value;
				}
				break;
			}
		}

		return new InvoicePrintedTotals(
			found.TryGetValue("taxable", out decimal taxable) ? taxable : found.TryGetValue("subtotal", out decimal sub) ? sub : null,
			found.TryGetValue("cgst", out decimal cgst) ? cgst : null,
			found.TryGetValue("sgst", out decimal sgst) ? sgst : null,
			found.TryGetValue("igst", out decimal igst) ? igst : null,
			found.TryGetValue("round", out decimal round) ? round : null,
			found.TryGetValue("grand", out decimal grand) ? grand : null);
	}

	private static decimal? LastAmount(string text)
	{
		MatchCollection matches = PrintedMoney.Matches(text);
		if (matches.Count == 0)
		{
			return null;
		}
		Match last = matches[^1];
		if (!decimal.TryParse(last.Groups[2].Value.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
		{
			return null;
		}
		return last.Groups[1].Value.Contains('-') ? -value : value;
	}
}
