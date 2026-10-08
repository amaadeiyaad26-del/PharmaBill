using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;
using UglyToad.PdfPig;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PharmaBill.App.Services;

/// <summary>
/// Reads one or more distributor invoices from PDF text, rendered pages, or images.
/// Gemini is used when a key is saved. Otherwise the local text and Windows OCR path is used.
/// </summary>
public sealed class InvoiceDocumentExtractor(GeminiPurchaseImportService gemini, GeminiApiKeyStore apiKeys, IOcrTextRecognizer ocr)
{
	public Task<ExtractedPurchaseInvoice> ExtractAsync(string path, CancellationToken cancellationToken = default)
	{
		return ExtractAsync(path, allowGemini: true, cancellationToken);
	}

	public Task<ExtractedPurchaseInvoice> ExtractLocalAsync(string path, CancellationToken cancellationToken = default)
	{
		return ExtractAsync(path, allowGemini: false, cancellationToken);
	}

	private async Task<ExtractedPurchaseInvoice> ExtractAsync(string path, bool allowGemini, CancellationToken cancellationToken)
	{
		if (!File.Exists(path))
		{
			throw new FileNotFoundException("The invoice file could not be found.", path);
		}

		string extension = Path.GetExtension(path);
		try
		{
			string text = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
				? await ReadPdfAsync(path, cancellationToken)
				: await ocr.RecognizeAsync(await File.ReadAllBytesAsync(path, cancellationToken), cancellationToken);
			return InvoiceTextParser.Parse(text, Path.GetFileNameWithoutExtension(path));
		}
		catch (Exception localError) when (localError is not OperationCanceledException && allowGemini && HasGeminiKey())
		{
			try
			{
				return await gemini.ExtractAsync(path, cancellationToken);
			}
			catch (Exception remoteError) when (remoteError is not OperationCanceledException)
			{
				throw new InvalidOperationException("This invoice could not be read online or on this PC. Check the file and try again.", localError);
			}
		}
	}

	private bool HasGeminiKey()
	{
		try
		{
			return !string.IsNullOrWhiteSpace(apiKeys.Read());
		}
		catch
		{
			return false;
		}
	}

	private async Task<string> ReadPdfAsync(string path, CancellationToken cancellationToken)
	{
		string embedded = ReadEmbeddedText(path);
		if (embedded.Count(char.IsLetterOrDigit) >= 80)
		{
			return embedded;
		}

		string rendered = await RenderAndRecognizeAsync(path, cancellationToken);
		return string.IsNullOrWhiteSpace(rendered) ? embedded : rendered;
	}

	internal static string ReadEmbeddedText(string path)
	{
		StringBuilder builder = new StringBuilder();
		using UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(path);
		foreach (UglyToad.PdfPig.Content.Page page in document.GetPages())
		{
			string visual = JoinWordsIntoLines(page.GetWords());
			builder.AppendLine(string.IsNullOrWhiteSpace(visual) ? page.Text : visual);
		}

		return builder.ToString();
	}

	private static string JoinWordsIntoLines(IEnumerable<UglyToad.PdfPig.Content.Word> words)
	{
		List<PlacedWord> placed = words
			.Select(word => new PlacedWord(word.Text, word.BoundingBox.Left, (word.BoundingBox.Bottom + word.BoundingBox.Top) / 2.0))
			.Where(word => !string.IsNullOrWhiteSpace(word.Text))
			.OrderByDescending(word => word.MidY)
			.ThenBy(word => word.Left)
			.ToList();
		List<List<PlacedWord>> tight = new List<List<PlacedWord>>();
		foreach (PlacedWord word in placed)
		{
			if (tight.Count == 0 || Math.Abs(tight[^1][0].MidY - word.MidY) > 2.8)
			{
				tight.Add(new List<PlacedWord>());
			}

			tight[^1].Add(word);
		}

		HashSet<int> consumed = new HashSet<int>();
		List<(double Y, string Text)> visual = new List<(double Y, string Text)>();
		for (int index = 0; index < tight.Count; index++)
		{
			if (consumed.Contains(index) || !RangePatternExists(tight[index]))
			{
				continue;
			}

			double anchor = tight[index][0].MidY;
			List<PlacedWord> band = new List<PlacedWord>();
			for (int other = 0; other < tight.Count; other++)
			{
				if (other != index && RangePatternExists(tight[other]))
				{
					continue;
				}

				if (Math.Abs(tight[other][0].MidY - anchor) <= 12)
				{
					band.AddRange(tight[other]);
					consumed.Add(other);
				}
			}

			consumed.Add(index);
			visual.Add((anchor, JoinRow(band)));
		}

		for (int index = 0; index < tight.Count; index++)
		{
			if (!consumed.Contains(index))
			{
				visual.Add((tight[index][0].MidY, JoinRow(tight[index])));
			}
		}

		StringBuilder builder = new StringBuilder();
		foreach ((double _, string text) in visual.OrderByDescending(row => row.Y))
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				builder.AppendLine(text);
			}
		}

		return builder.ToString();
	}

	private static bool RangePatternExists(List<PlacedWord> row)
	{
		string text = string.Join(' ', row.OrderBy(word => word.Left).Select(word => word.Text));
		return Regex.IsMatch(text, @"\b(0?[1-9]|1[0-2])\s*/\s*(\d{2}|\d{4})\s*[-–]\s*(0?[1-9]|1[0-2])\s*/\s*(\d{2}|\d{4})\b");
	}

	private static string JoinRow(List<PlacedWord> row)
	{
		return string.Join(' ', row.OrderBy(word => word.Left).ThenByDescending(word => word.MidY).Select(word => word.Text.Trim()));
	}

	private readonly record struct PlacedWord(string Text, double Left, double MidY);

	private async Task<string> RenderAndRecognizeAsync(string path, CancellationToken cancellationToken)
	{
		StringBuilder builder = new StringBuilder();
		try
		{
			StorageFile file = await StorageFile.GetFileFromPathAsync(path);
			global::Windows.Data.Pdf.PdfDocument pdf = await global::Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
			uint pages = Math.Min(pdf.PageCount, 6);
			for (uint index = 0; index < pages; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				using PdfPage page = pdf.GetPage(index);
				using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
				PdfPageRenderOptions options = new PdfPageRenderOptions { DestinationWidth = 1600 };
				await page.RenderToStreamAsync(stream, options);
				stream.Seek(0);
				using Stream net = stream.AsStreamForRead();
				using MemoryStream memory = new MemoryStream();
				await net.CopyToAsync(memory, cancellationToken);
				string pageText = await ocr.RecognizeAsync(memory.ToArray(), cancellationToken);
				if (!string.IsNullOrWhiteSpace(pageText))
				{
					builder.AppendLine(pageText);
				}
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			builder.AppendLine(ex.Message);
		}

		return builder.ToString();
	}
}

public static class InvoiceTextParser
{
	private static readonly Regex RangePattern = new Regex(@"\b(0?[1-9]|1[0-2])\s*/\s*(\d{2}|\d{4})\s*[-–]\s*(0?[1-9]|1[0-2])\s*/\s*(\d{2}|\d{4})\b", RegexOptions.Compiled);

	private static readonly Regex SingleExpiryPattern = new Regex(@"\b(0?[1-9]|1[0-2])\s*/\s*(\d{2}|\d{4})\b", RegexOptions.Compiled);

	private static readonly Regex InvoiceNoPattern = new Regex(@"invoice\s*(?:no\.?|number|#)\s*[:\-]?\s*([A-Z0-9][A-Z0-9/\-]{2,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex LabeledDatePattern = new Regex(@"\bdate\s*[:\-]?\s*(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{2,4})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex PackPattern = new Regex(@"^\d+\s*[xX×]\s*\d+(?:\s+\S+){0,2}$", RegexOptions.Compiled);

	private static readonly Regex BatchPattern = new Regex(@"^[A-Z]{2,}[0-9]{2,}[A-Z0-9]*$", RegexOptions.Compiled);

	private static readonly Regex MoneyTokenPattern = new Regex(@"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?", RegexOptions.Compiled);

	public static ExtractedPurchaseInvoice Parse(string? text, string fallbackName)
	{
		string[] lines = (text ?? string.Empty).Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (lines.Length == 0)
		{
			throw new InvalidDataException("No text could be read from '" + fallbackName + "'.");
		}

		string supplier = GuessSupplier(lines) ?? fallbackName;
		string invoiceNo = MatchInvoiceNo(lines) ?? fallbackName;
		string invoiceDate = GuessDate(lines);
		decimal gst = GuessGst(text ?? string.Empty);
		List<ExtractedPurchaseLine> items = new List<ExtractedPurchaseLine>();
		List<decimal> discounts = new List<decimal>();
		int tableStart = FindTableStart(lines);
		int cursor = tableStart;
		while (cursor < lines.Length)
		{
			if (!TryReadItem(lines, ref cursor, gst, out ExtractedPurchaseLine? item, out decimal discount) || item == null)
			{
				cursor++;
				continue;
			}

			items.Add(item);
			discounts.Add(discount);
		}

		if (items.Count == 0)
		{
			throw new InvalidDataException("No medicine rows with a batch expiry were found in '" + fallbackName + "'.");
		}

		decimal computed = items.Sum(item => item.Amount > 0m ? item.Amount : decimal.Round(item.Quantity * item.Rate, 2, MidpointRounding.AwayFromZero));
		decimal subtotal = FindLabeledAmount(lines, "Taxable value") ?? computed;
		decimal grand = FindLabeledAmount(lines, "Grand total") ?? decimal.Round(subtotal + (subtotal * gst / 100m), 2, MidpointRounding.AwayFromZero);
		ExtractedPurchaseInvoice invoice = new ExtractedPurchaseInvoice("PURCHASE_INVOICE", supplier, invoiceNo, invoiceDate, items, subtotal, grand);
		InvoiceTextParserCache.Discounts.Add(invoice, discounts);
		return invoice;
	}

	private static bool TryReadItem(string[] lines, ref int cursor, decimal gst, out ExtractedPurchaseLine? item, out decimal discountPercent)
	{
		item = null;
		discountPercent = 0m;
		string line = lines[cursor];
		Match range = RangePattern.Match(line);
		if (range.Success && line.Length > range.Length + 8)
		{
			if (!TryParseWideRow(line, range, gst, out item))
			{
				return false;
			}

			cursor++;
			return true;
		}

		if (!range.Success || !IsMostlyDateRange(line, range))
		{
			return false;
		}

		int start = cursor;
		while (start > 0 && !IsTableBoundary(lines[start - 1]) && !TryMoneyLine(lines[start - 1], out _))
		{
			start--;
		}

		List<string> cells = lines[start..cursor].Where(cell => !IsColumnHeader(cell)).ToList();
		if (!TrySplitCells(cells, out string name, out string batch))
		{
			return false;
		}

		string expiry = FormatMonthYear(range.Groups[3].Value, range.Groups[4].Value);
		int after = cursor + 1;
		List<decimal> numbers = new List<decimal>();
		while (after < lines.Length && numbers.Count < 3 && TryMoneyLine(lines[after], out decimal money))
		{
			numbers.Add(money);
			after++;
		}

		if (numbers.Count < 2)
		{
			return false;
		}

		decimal quantity = numbers[0];
		decimal rate = numbers[1];
		decimal amount = numbers.Count > 2 ? numbers[2] : decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
		item = new ExtractedPurchaseLine(name, batch, expiry, quantity, 0m, 0m, rate, gst, amount);
		cursor = after;
		return true;
	}

	private static bool TryParseWideRow(string line, Match range, decimal gst, out ExtractedPurchaseLine? item)
	{
		item = null;
		string before = line[..range.Index].Trim();
		string after = line[(range.Index + range.Length)..];
		Match batchMatch = Regex.Match(before, @"\b([A-Z]{2,}[0-9]{2,}[A-Z0-9]*)\s*$");
		if (!batchMatch.Success)
		{
			return false;
		}

		string batch = batchMatch.Groups[1].Value;
		string head = before[..batchMatch.Index].Trim();
		head = Regex.Replace(head, @"\s+\d+\s*[xX×]\s*\d+(?:\s+\S+){0,3}\s*$", string.Empty).Trim();
		head = StripTrailingCompany(head);
		head = Regex.Replace(head, @"^\d{1,2}\s+", string.Empty).Trim();
		List<decimal> numbers = MoneyTokenPattern.Matches(after).Select(match => decimal.Parse(match.Value.Replace(",", string.Empty), CultureInfo.InvariantCulture)).ToList();
		if (head.Length < 2 || numbers.Count < 2)
		{
			return false;
		}

		decimal quantity = numbers[0];
		decimal rate = numbers[1];
		decimal amount = numbers.Count > 2 ? numbers[2] : decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
		item = new ExtractedPurchaseLine(head, batch, FormatMonthYear(range.Groups[3].Value, range.Groups[4].Value), quantity, 0m, 0m, rate, gst, amount);
		return true;
	}

	private static string StripTrailingCompany(string head)
	{
		string[] parts = head.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
		int limit = Math.Min(6, parts.Length);
		for (int length = limit; length >= 1; length--)
		{
			string tail = string.Join(' ', parts[^length..]);
			bool companyEnding = Regex.IsMatch(parts[^1], @"^(ltd\.?|limited|healthcare)$", RegexOptions.IgnoreCase)
				|| Regex.IsMatch(tail, @"pvt\.?\s*ltd\.?$", RegexOptions.IgnoreCase);
			if (companyEnding && IsManufacturer(tail))
			{
				return string.Join(' ', parts[..^length]);
			}
		}

		return head;
	}

	private static bool TrySplitCells(IReadOnlyList<string> cells, out string name, out string batch)
	{
		name = string.Empty;
		batch = string.Empty;
		if (cells.Count < 2)
		{
			return false;
		}

		int end = cells.Count - 1;
		if (!BatchPattern.IsMatch(cells[end]))
		{
			return false;
		}

		batch = cells[end];
		end--;
		if (end >= 0 && PackPattern.IsMatch(cells[end]))
		{
			end--;
		}

		if (end >= 0 && IsManufacturer(cells[end]))
		{
			end--;
		}

		int start = 0;
		if (start <= end && Regex.IsMatch(cells[start], @"^\d{1,2}$"))
		{
			start++;
		}

		if (start > end)
		{
			return false;
		}

		name = string.Join(' ', cells.Skip(start).Take(end - start + 1));
		return name.Length >= 2;
	}

	private static bool IsMostlyDateRange(string line, Match range) => line.Trim().Length <= range.Length + 4;

	private static bool IsTableBoundary(string line)
	{
		if (RangePattern.IsMatch(line) || IsColumnHeader(line))
		{
			return true;
		}

		return line.Contains("taxable", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("grand total", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("amount in words", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("declaration", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsColumnHeader(string line)
	{
		return line.Contains("batch", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("mfg", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("product", StringComparison.OrdinalIgnoreCase)
			|| line.Equals("qty", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("amount", StringComparison.OrdinalIgnoreCase)
			|| line.Contains("manufacturer", StringComparison.OrdinalIgnoreCase)
			|| line.Equals("pack", StringComparison.OrdinalIgnoreCase)
			|| line.Equals("sl", StringComparison.OrdinalIgnoreCase);
	}

	private static int FindTableStart(string[] lines)
	{
		for (int index = 0; index < lines.Length; index++)
		{
			if (lines[index].Contains("batch", StringComparison.OrdinalIgnoreCase) && (lines[index].Contains("mfg", StringComparison.OrdinalIgnoreCase) || lines[index].Contains("exp", StringComparison.OrdinalIgnoreCase)))
			{
				return index + 1;
			}
		}

		return 0;
	}

	private static bool IsManufacturer(string line) =>
		Regex.IsMatch(line, @"\b(ltd|pvt|labs|healthcare|pharma|remedies|laboratories|limited)\b", RegexOptions.IgnoreCase);

	private static bool TryMoneyLine(string line, out decimal value)
	{
		value = 0m;
		string text = line.Trim();
		if (text.Contains('/') || text.Contains('-') || RangePattern.IsMatch(text) || SingleExpiryPattern.IsMatch(text))
		{
			return false;
		}

		if (text.StartsWith("Rs", StringComparison.OrdinalIgnoreCase))
		{
			text = Regex.Replace(text, @"^Rs\.?\s*", string.Empty, RegexOptions.IgnoreCase);
		}

		text = text.Replace(",", string.Empty);
		return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
	}

	private static string FormatMonthYear(string monthText, string yearText)
	{
		int month = int.Parse(monthText, CultureInfo.InvariantCulture);
		int year = int.Parse(yearText, CultureInfo.InvariantCulture);
		if (yearText.Length <= 2)
		{
			year += 2000;
		}

		return month.ToString("00", CultureInfo.InvariantCulture) + "/" + year.ToString(CultureInfo.InvariantCulture);
	}

	private static decimal GuessGst(string text)
	{
		if (Regex.IsMatch(text, @"CGST\s*@\s*2\.5", RegexOptions.IgnoreCase) && Regex.IsMatch(text, @"SGST\s*@\s*2\.5", RegexOptions.IgnoreCase))
		{
			return 5m;
		}

		Match match = Regex.Match(text, @"GST\s*@\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
		if (match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal gst))
		{
			return gst;
		}

		return 0m;
	}

	private static string? GuessSupplier(string[] lines)
	{
		for (int index = 0; index < lines.Length - 1; index++)
		{
			if (lines[index].Contains("seller", StringComparison.OrdinalIgnoreCase) && lines[index].Contains("wholesaler", StringComparison.OrdinalIgnoreCase))
			{
				return CleanSupplier(lines[index + 1]);
			}
		}

		for (int index = 0; index < lines.Length; index++)
		{
			if (lines[index].StartsWith("M/s ", StringComparison.OrdinalIgnoreCase) || lines[index].StartsWith("M/s", StringComparison.OrdinalIgnoreCase))
			{
				return CleanSupplier(lines[index]);
			}
		}

		return null;
	}

	private static string CleanSupplier(string line)
	{
		Match named = Regex.Match(line, @"M/s\.?\s*(.+?)(?=\s+M/s\.?\s+|\s+Date\s*:|\s+Invoice\s*No|$)", RegexOptions.IgnoreCase);
		string name = named.Success
			? named.Groups[1].Value.Trim()
			: Regex.Replace(line.Trim(), @"^M/s\.?\s*", string.Empty, RegexOptions.IgnoreCase).Trim();
		int date = name.IndexOf("Date", StringComparison.OrdinalIgnoreCase);
		if (date > 0)
		{
			name = name[..date].Trim();
		}

		return name.Length > 80 ? name[..80] : name;
	}

	private static string? MatchInvoiceNo(string[] lines)
	{
		foreach (string line in lines)
		{
			Match match = InvoiceNoPattern.Match(line);
			if (match.Success)
			{
				return match.Groups[1].Value.Trim().TrimEnd('.', ',', ';');
			}
		}

		return null;
	}

	private static string GuessDate(string[] lines)
	{
		foreach (string line in lines)
		{
			Match match = LabeledDatePattern.Match(line);
			if (!match.Success)
			{
				continue;
			}

			int day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
			int month = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
			int year = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
			if (match.Groups[3].Value.Length == 2)
			{
				year += 2000;
			}

			if (month is < 1 or > 12 || day is < 1 or > 31)
			{
				continue;
			}

			return new DateOnly(year, month, Math.Min(day, DateTime.DaysInMonth(year, month))).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		}

		return DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	}

	private static decimal? FindLabeledAmount(string[] lines, string label)
	{
		for (int index = 0; index < lines.Length; index++)
		{
			if (!lines[index].Contains(label, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (TryMoneyLine(lines[index], out decimal sameLine) && lines[index].Contains("Rs", StringComparison.OrdinalIgnoreCase))
			{
				Match inline = Regex.Match(lines[index], @"([\d,]+(?:\.\d+)?)");
				if (inline.Success && decimal.TryParse(inline.Groups[1].Value.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed))
				{
					return parsed;
				}

				return sameLine;
			}

			for (int next = index + 1; next < Math.Min(lines.Length, index + 3); next++)
			{
				if (TryMoneyLine(lines[next].Replace("Rs.", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("Rs", string.Empty, StringComparison.OrdinalIgnoreCase), out decimal amount))
				{
					return amount;
				}
			}
		}

		return null;
	}
}

/// <summary>Holds discount percents that the shared invoice record does not store.</summary>
internal static class InvoiceTextParserCache
{
	public static readonly ConditionalWeakTable<ExtractedPurchaseInvoice, List<decimal>> Discounts = new ConditionalWeakTable<ExtractedPurchaseInvoice, List<decimal>>();
}
