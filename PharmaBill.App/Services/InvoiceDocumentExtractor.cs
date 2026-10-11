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

		string workingPath = path;
		string extension = Path.GetExtension(path);
		bool isPdf = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
		if (!isPdf)
		{
			workingPath = await InvoiceImageNormalizer.EnsureSupportedAsync(path, cancellationToken);
		}

		// Phone camera photos and scanned invoices: prefer Gemini vision when a key is configured.
		if (allowGemini && HasGeminiKey())
		{
			try
			{
				System.Diagnostics.Debug.WriteLine("Invoice OCR: Gemini vision key present — sending image bytes from " + workingPath);
				ExtractedPurchaseInvoice remote = await gemini.ExtractAsync(workingPath, cancellationToken);
				System.Diagnostics.Debug.WriteLine("Invoice OCR: Gemini returned " + remote.Items.Count + " line(s).");
				return remote;
			}
			catch (Exception remoteError) when (remoteError is not OperationCanceledException)
			{
				System.Diagnostics.Debug.WriteLine("Invoice OCR: Gemini failed, falling back to local OCR. " + remoteError);
				try
				{
					return await ExtractLocalCoreAsync(workingPath, isPdf, cancellationToken);
				}
				catch (Exception localError) when (localError is not OperationCanceledException)
				{
					throw new InvalidOperationException(
						"This invoice could not be read online or on this PC. Gemini: " + remoteError.Message
						+ " Local: " + localError.Message,
						remoteError);
				}
			}
		}

		if (allowGemini)
		{
			System.Diagnostics.Debug.WriteLine("Invoice OCR: no Gemini API key — using local Windows OCR / PDF text.");
		}

		return await ExtractLocalCoreAsync(workingPath, isPdf, cancellationToken);
	}

	private async Task<ExtractedPurchaseInvoice> ExtractLocalCoreAsync(string path, bool isPdf, CancellationToken cancellationToken)
	{
		byte[]? imageBytes = isPdf ? null : await File.ReadAllBytesAsync(path, cancellationToken);
		string text = isPdf
			? await ReadPdfAsync(path, cancellationToken)
			: await ocr.RecognizeAsync(imageBytes!, cancellationToken);
		InvoiceTextParser.WriteOcrDebug(text);
		if (!isPdf)
		{
			IReadOnlyList<OcrWordPosition> words = await ocr.RecognizeWordsAsync(imageBytes!, cancellationToken);
			ExtractedPurchaseInvoice positioned = InvoiceTextParser.ParsePositioned(words, text, Path.GetFileNameWithoutExtension(path));
			if (positioned.Items.Count > 0)
			{
				return positioned;
			}
		}

		return InvoiceTextParser.Parse(text, Path.GetFileNameWithoutExtension(path));
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
		return InvoiceTextParser.HasCompositeOrExpiryDate(text);
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
	private sealed record PositionedColumns(
		double Product,
		double Batch,
		double Expiry,
		double Quantity,
		double Rate,
		double Amount,
		double? Manufacturer,
		double? Pack);

	/// <summary>Mfg/Exp composites: 07/25 - 06/27, OCR bullets (07/26 • 06/27), 07/25/06/27.</summary>
	private static readonly Regex RangePattern = new Regex(
		@"\b(0?[1-9]|1[0-2])\s*[/\-.]\s*(\d{2}|\d{4})\s*[-–—/•·.]\s*(0?[1-9]|1[0-2])\s*[/\-.]?\s*(\d{2}|\d{4})\b",
		RegexOptions.Compiled);

	/// <summary>Dosage strength tokens that must never be read as invoice Qty.</summary>
	private static readonly Regex DosageStrengthPattern = new Regex(
		@"\b\d+(?:\.\d+)?\s*(?:mg|mcg|µg|ug|ml|gm|g|iu|units?)\b",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex SingleExpiryPattern = new Regex(
		@"\b(0?[1-9]|1[0-2])\s*[/\-.]\s*(\d{2}|\d{4})\b",
		RegexOptions.Compiled);

	private static readonly Regex LabeledExpPattern = new Regex(
		@"(?:exp(?:iry)?|e\.?x\.?p\.?)\s*[:\-]?\s*(0?[1-9]|1[0-2])\s*[/\-.]\s*(\d{2}|\d{4})",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex InvoiceNoPattern = new Regex(
		@"(?:invoice|bill)\s*(?:no\.?|number|#)\s*[:\-]?\s*([A-Z0-9][A-Z0-9/\-]{2,})",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex LabeledDatePattern = new Regex(
		@"\b(?:date|dated)\s*[:\-]?\s*(\d{1,2})[./\-](\d{1,2})[./\-](\d{2,4})\b",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly Regex LooseDatePattern = new Regex(
		@"\b(\d{1,2})[./\-](\d{1,2})[./\-](\d{2,4})\b",
		RegexOptions.Compiled);

	private static readonly Regex PackPattern = new Regex(@"^\d+\s*[xX×]\s*\d+(?:\s+\S+){0,2}$", RegexOptions.Compiled);

	private static readonly Regex BatchPattern = new Regex(@"^[A-Z]{2,}[0-9]{2,}[A-Z0-9]*$", RegexOptions.Compiled);

	private static readonly Regex BatchTokenPattern = new Regex(@"\b([A-Z]{2,}[0-9]{2,}[A-Z0-9]*)\b", RegexOptions.Compiled);

	private static readonly Regex MoneyTokenPattern = new Regex(@"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?", RegexOptions.Compiled);

	public static bool HasCompositeOrExpiryDate(string text) =>
		RangePattern.IsMatch(text) || LabeledExpPattern.IsMatch(text) || SingleExpiryPattern.IsMatch(text);

	public static ExtractedPurchaseInvoice ParsePositioned(
		IReadOnlyList<OcrWordPosition> words,
		string? rawText,
		string fallbackName)
	{
		ArgumentNullException.ThrowIfNull(words);
		if (!TryFindPositionedColumns(words, out PositionedColumns columns, out double headerY))
		{
			return Parse(rawText, fallbackName);
		}

		List<OcrWordPosition> usableWords = words
			.Where(word => word.Y > headerY)
			.ToList();
		double? footerY = GroupPositionedLines(usableWords)
			.Where(line => line.Text.Contains("taxable value", StringComparison.OrdinalIgnoreCase)
				|| line.Text.Contains("cgst", StringComparison.OrdinalIgnoreCase)
				|| line.Text.Contains("sgst", StringComparison.OrdinalIgnoreCase)
				|| line.Text.Contains("grand total", StringComparison.OrdinalIgnoreCase)
				|| line.Text.Contains("amount in words", StringComparison.OrdinalIgnoreCase))
			.Select(line => (double?)line.Y)
			.OrderBy(value => value)
			.FirstOrDefault();
		if (footerY.HasValue)
		{
			usableWords = usableWords.Where(word => word.Y + word.Height / 2 < footerY.Value - 8).ToList();
		}

		List<(int Number, double Y)> serials = usableWords
			.Where(word => word.X + word.Width / 2 < columns.Product - 4)
			.Select(word => (word.Text.Trim(), Y: word.Y + word.Height / 2))
			.Where(value => int.TryParse(value.Item1, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
				&& number is >= 1 and <= 99)
			.Select(value => (int.Parse(value.Item1, CultureInfo.InvariantCulture), value.Y))
			.OrderBy(value => value.Y)
			.ToList();
		List<(int Number, double Y)> orderedSerials = new List<(int Number, double Y)>();
		int expectedSerial = 1;
		foreach ((int number, double y) in serials)
		{
			if (number == expectedSerial)
			{
				orderedSerials.Add((number, y));
				expectedSerial++;
			}
		}

		if (orderedSerials.Count == 0)
		{
			return Parse(rawText, fallbackName);
		}

		List<ExtractedPurchaseLine> items = new List<ExtractedPurchaseLine>();
		foreach (int index in Enumerable.Range(0, orderedSerials.Count))
		{
			double startY = index == 0
				? orderedSerials[index].Y - 30
				: (orderedSerials[index - 1].Y + orderedSerials[index].Y) / 2;
			double endY = index + 1 < orderedSerials.Count
				? (orderedSerials[index].Y + orderedSerials[index + 1].Y) / 2
				: double.MaxValue;
			List<OcrWordPosition> row = usableWords
				.Where(word =>
				{
					double centerY = word.Y + word.Height / 2;
					return centerY >= startY && centerY < endY;
				})
				.ToList();

			string itemName = JoinColumn(row, columns.Product, columns.Manufacturer ?? columns.Pack ?? columns.Batch);
			if (string.IsNullOrWhiteSpace(itemName) || !RecognizedMedicinePattern.IsMatch(itemName))
			{
				continue;
			}

			string batch = ExtractPositionedBatch(row, columns);
			string expiry = ExtractExpiry(JoinColumn(row, columns.Expiry, columns.Quantity));
			decimal quantity = ExtractPositionedNumber(row, columns.Quantity, columns.Rate, wholeNumbersOnly: true);
			decimal rate = ExtractPositionedNumber(row, columns.Rate, columns.Amount, wholeNumbersOnly: false);
			decimal amount = ExtractPositionedNumber(row, columns.Amount, null, wholeNumbersOnly: false);
			string? warning = quantity > 0m && rate > 0m && amount > 0m
				&& Math.Abs(decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero) - amount) > 0.01m
				? "Quantity x rate does not equal amount."
				: null;

			items.Add(new ExtractedPurchaseLine(
				NormalizeMedicineName(itemName),
				string.IsNullOrWhiteSpace(batch) ? "NA" : batch,
				expiry,
				quantity,
				0m,
				0m,
				rate,
				0m,
				amount)
			{
				ValidationWarning = warning
			});
		}

		if (items.Count == 0)
		{
			return Parse(rawText, fallbackName);
		}

		ExtractedPurchaseInvoice metadata = Parse(rawText, fallbackName);
		return new ExtractedPurchaseInvoice(
			metadata.DocumentType,
			metadata.Supplier,
			metadata.InvoiceNo,
			metadata.InvoiceDate,
			items,
			items.Sum(item => item.Amount),
			metadata.GrandTotal);
	}

	private static bool TryFindPositionedColumns(
		IReadOnlyList<OcrWordPosition> words,
		out PositionedColumns columns,
		out double headerY)
	{
		List<OcrWordPosition> candidates = words
			.Where(word => !string.IsNullOrWhiteSpace(word.Text))
			.OrderBy(word => word.Y)
			.ToList();
		OcrWordPosition? product = candidates.FirstOrDefault(word => word.Text.Equals("Product", StringComparison.OrdinalIgnoreCase));
		OcrWordPosition? batch = candidates.FirstOrDefault(word => word.Text.StartsWith("Batch", StringComparison.OrdinalIgnoreCase));
		OcrWordPosition? expiry = candidates.FirstOrDefault(word => word.Text.Equals("Mfg", StringComparison.OrdinalIgnoreCase)
			|| word.Text.Equals("Exp", StringComparison.OrdinalIgnoreCase));
		OcrWordPosition? quantity = candidates.FirstOrDefault(word => word.Text.Equals("Qty", StringComparison.OrdinalIgnoreCase));
		OcrWordPosition? rate = candidates.FirstOrDefault(word => word.Text.Equals("Rate", StringComparison.OrdinalIgnoreCase));
		OcrWordPosition? amount = candidates.FirstOrDefault(word => word.Text.Equals("Amount", StringComparison.OrdinalIgnoreCase));
		if (product == null || batch == null || expiry == null || quantity == null || rate == null || amount == null)
		{
			columns = null!;
			headerY = 0;
			return false;
		}

		OcrWordPosition? manufacturer = candidates.FirstOrDefault(word => word.Text.StartsWith("Manufact", StringComparison.OrdinalIgnoreCase));
		OcrWordPosition? pack = candidates.FirstOrDefault(word => word.Text.StartsWith("Pack", StringComparison.OrdinalIgnoreCase));
		columns = new PositionedColumns(
			product.X,
			batch.X,
			expiry.X,
			quantity.X,
			rate.X,
			amount.X,
			manufacturer == null ? null : manufacturer.X,
			pack == null ? null : pack.X);
		headerY = new[] { product, batch, expiry, quantity, rate, amount }
			.Min(word => word.Y);
		return columns.Product < columns.Batch
			&& columns.Batch < columns.Expiry
			&& columns.Expiry < columns.Quantity
			&& columns.Quantity < columns.Rate
			&& columns.Rate < columns.Amount;
	}

	private static string ExtractPositionedBatch(IEnumerable<OcrWordPosition> row, PositionedColumns columns)
	{
		string value = JoinColumn(row, columns.Batch, columns.Expiry);
		string compact = Regex.Replace(value, @"\s+", string.Empty);
		return TryExtractPlausibleBatch(compact, out string batch) ? batch : compact.Trim();
	}

	private static decimal ExtractPositionedNumber(
		IEnumerable<OcrWordPosition> row,
		double start,
		double? end,
		bool wholeNumbersOnly)
	{
		List<string> values = row
			.Where(word =>
			{
				double center = Center(word);
				return center >= start && (!end.HasValue || center < end.Value);
			})
			.OrderBy(word => word.X)
			.Select(word => word.Text.Replace(",", string.Empty).Trim())
			.ToList();
		
		string combined = string.Concat(values);
		if (values.Count > 1
			&& decimal.TryParse(combined, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal combinedValue)
			&& (!wholeNumbersOnly || combinedValue == decimal.Truncate(combinedValue)))
		{
			return combinedValue;
		}

		foreach (string value in values)
		{
			if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
				&& (!wholeNumbersOnly || parsed == decimal.Truncate(parsed)))
			{
				return parsed;
			}
		}

		return 0m;
	}

	private static string JoinColumn(IEnumerable<OcrWordPosition> row, double start, double end)
	{
		return string.Join(
			' ',
			row.Where(word => Center(word) >= start && Center(word) < end)
				.OrderBy(word => word.X)
				.Select(word => word.Text.Trim()));
	}

	private static double Center(OcrWordPosition word) => word.X + word.Width / 2;

	private static IEnumerable<(double Y, string Text)> GroupPositionedLines(IEnumerable<OcrWordPosition> words)
	{
		List<OcrWordPosition> ordered = words.OrderBy(word => word.Y).ThenBy(word => word.X).ToList();
		List<OcrWordPosition> current = new List<OcrWordPosition>();
		double lineY = 0;
		foreach (OcrWordPosition word in ordered)
		{
			double centerY = word.Y + word.Height / 2;
			if (current.Count > 0 && Math.Abs(centerY - lineY) > 8)
			{
				yield return (lineY, string.Join(' ', current.OrderBy(item => item.X).Select(item => item.Text)));
				current.Clear();
			}

			if (current.Count == 0)
			{
				lineY = centerY;
			}

			current.Add(word);
		}

		if (current.Count > 0)
		{
			yield return (lineY, string.Join(' ', current.OrderBy(item => item.X).Select(item => item.Text)));
		}
	}

	public static void WriteOcrDebug(string? rawOcrText)
	{
		string text = rawOcrText ?? string.Empty;
		System.Diagnostics.Debug.WriteLine("=== Invoice OCR raw text (" + text.Length + " chars) ===");
		System.Diagnostics.Debug.WriteLine(text);
		try
		{
			string path = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"PharmaBill",
				"ocr_debug_raw.txt");
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, text);
			// Also drop a copy next to the process for quick inspection during QA.
			File.WriteAllText("ocr_debug_raw.txt", text);
			System.Diagnostics.Debug.WriteLine("Invoice OCR debug written to " + path);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("Invoice OCR debug write failed: " + ex.Message);
		}
	}

	public static ExtractedPurchaseInvoice Parse(string? text, string fallbackName)
	{
		WriteOcrDebug(text);
		string[] lines = (text ?? string.Empty).Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (lines.Length == 0)
		{
			throw new InvalidDataException("No text could be read from '" + fallbackName + "'.");
		}

		string supplier = GuessSupplier(lines) ?? "Supplier";
		string invoiceNo = MatchInvoiceNo(lines) ?? "UNKNOWN";
		string invoiceDate = GuessDate(lines);
		decimal gst = GuessGst(text ?? string.Empty);
		int tableStart = FindTableStart(lines);
		int tableEnd = FindTableEnd(lines, tableStart);
		string[] tableLines = lines[tableStart..tableEnd];

		List<ExtractedPurchaseLine> items = new List<ExtractedPurchaseLine>();
		List<decimal> discounts = new List<decimal>();

		void AddCandidate(ExtractedPurchaseLine candidate, decimal discount)
		{
		candidate = candidate with { ItemName = NormalizeMedicineName(candidate.ItemName) };
		if (!IsAcceptableMedicineLine(candidate.ItemName))
		{
			return;
		}

		string name = Regex.Replace(candidate.ItemName.Trim(), @"\s+", " ");
		string candidateIdentity = GetMedicineIdentity(name);
		int existingIndex = items.FindIndex(existing =>
		{
			string existingName = Regex.Replace(existing.ItemName.Trim(), @"\s+", " ");
			string existingIdentity = GetMedicineIdentity(existingName);
			return string.Equals(existingName, name, StringComparison.OrdinalIgnoreCase)
				|| (!string.IsNullOrWhiteSpace(candidateIdentity)
					&& string.Equals(existingIdentity, candidateIdentity, StringComparison.OrdinalIgnoreCase));
		});
		if (existingIndex < 0)
		{
			items.Add(candidate);
			discounts.Add(discount);
			return;
		}

		ExtractedPurchaseLine existing = items[existingIndex];
		items[existingIndex] = existing with
		{
			Batch = string.IsNullOrWhiteSpace(existing.Batch) || existing.Batch == "NA" ? candidate.Batch : existing.Batch,
			Expiry = string.IsNullOrWhiteSpace(existing.Expiry) ? candidate.Expiry : existing.Expiry,
			Quantity = existing.Quantity > 0m ? existing.Quantity : candidate.Quantity,
			Mrp = existing.Mrp > 0m ? existing.Mrp : candidate.Mrp,
			Rate = existing.Rate > 0m ? existing.Rate : candidate.Rate,
			Amount = existing.Amount > 0m ? existing.Amount : candidate.Amount
		};
		if (discounts[existingIndex] == 0m)
		{
			discounts[existingIndex] = discount;
		}
		}

		// Try every layout. OCR frequently produces a partially columnar table, so
		// stopping after the first successful strategy can silently lose rows.
		if (LooksLikeColumnarInvoice(tableLines))
		{
		foreach (ExtractedPurchaseLine columnar in TryParseColumnarTable(tableLines, gst))
		{
			AddCandidate(columnar, 0m);
		}
		}

		{
		int cursor = 0;
		while (cursor < tableLines.Length)
		{
			if (IsNonMedicineNoise(tableLines[cursor]))
			{
				cursor++;
					continue;
				}

				if (!TryReadItem(tableLines, ref cursor, gst, out ExtractedPurchaseLine? item, out decimal discount) || item == null)
				{
					cursor++;
					continue;
				}

			AddCandidate(item, discount);
		}
		}

		{
		int cursor = 0;
		while (cursor < tableLines.Length)
		{
			if (IsNonMedicineNoise(tableLines[cursor]))
			{
				cursor++;
					continue;
				}

				if (!TryReadLooseItem(tableLines, ref cursor, gst, out ExtractedPurchaseLine? loose) || loose == null)
				{
					cursor++;
					continue;
				}

				AddCandidate(loose, 0m);
			}
		}

		foreach (ExtractedPurchaseLine columnar in TryParseColumnarTable(tableLines, gst))
		{
			AddCandidate(columnar, 0m);
		}

		foreach (ExtractedPurchaseLine forgiving in ParseForgivingLines(tableLines, gst))
		{
			AddCandidate(forgiving, 0m);
		}

		int recognizedMedicineLineCount = lines.Count(line =>
			RecognizedMedicinePattern.IsMatch(line) && !IsNonMedicineNoise(line));
		if (items.Count < recognizedMedicineLineCount)
		{
			foreach (ExtractedPurchaseLine recovered in RecoverRecognizedMedicineLines(lines, gst))
			{
				AddCandidate(recovered, 0m);
			}
		}

		if (items.Count == 0)
		{
			throw new InvalidDataException(
				"No medicine rows were found in '" + fallbackName + "'. "
				+ "Check %LocalAppData%\\PharmaBill\\ocr_debug_raw.txt for the OCR text; try a clearer photo or enter lines manually.");
		}

		decimal computed = items.Sum(item => item.Amount > 0m ? item.Amount : decimal.Round(item.Quantity * item.Rate, 2, MidpointRounding.AwayFromZero));
		decimal subtotal = FindLabeledAmount(lines, "Taxable value") ?? computed;
		decimal grand = FindLabeledAmount(lines, "Grand total") ?? decimal.Round(subtotal + (subtotal * gst / 100m), 2, MidpointRounding.AwayFromZero);
		ExtractedPurchaseInvoice invoice = new ExtractedPurchaseInvoice("PURCHASE_INVOICE", supplier, invoiceNo, invoiceDate, items, subtotal, grand);
		InvoiceTextParserCache.Discounts.Add(invoice, discounts);
		return invoice;
	}

	private static readonly Regex RecognizedMedicinePattern = new Regex(
		@"\b(?:paracetamol|amoxicillin|pantoprazole|cetirizine|azithromycin|metformin)\b|"
		+ @"\b(?:tablets?|capsules?|syrup)\b",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static string GetMedicineIdentity(string name)
	{
		Match match = Regex.Match(
			name,
			@"\b(paracetamol|amoxicillin|pantoprazole|cetirizine|azithromycin|metformin)\b",
			RegexOptions.IgnoreCase);
		return match.Success ? match.Value : string.Empty;
	}

	public static string NormalizeMedicineName(string name)
	{
		string cleaned = name.Replace('æ', 'a').Replace('Æ', 'A');
		// Pull distributor batch codes (AMC2512 / MET2511) out of the product label into Batch elsewhere.
		cleaned = Regex.Replace(
			cleaned,
			@"\b([A-Za-z]{2,4}\d{4,6}[A-Za-z0-9]*)\b",
			" ",
			RegexOptions.IgnoreCase);
		cleaned = Regex.Replace(
			cleaned,
			@"\b(?:sch(?:edule)?\.?\s*[Hh]1?|h1)\b",
			" ",
			RegexOptions.IgnoreCase);
		return Regex.Replace(cleaned, @"\s+", " ").Trim();
	}

	private static readonly Regex LooseBatchPattern = new Regex(@"\b[A-Z0-9]{5,10}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static IEnumerable<ExtractedPurchaseLine> RecoverRecognizedMedicineLines(string[] lines, decimal gst)
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index].Trim();
			if (!RecognizedMedicinePattern.IsMatch(line) || IsNonMedicineNoise(line))
			{
				continue;
			}

			string combined = index + 1 < lines.Length
				? line + " " + lines[index + 1].Trim()
				: line;
			string batch = LooseBatchPattern.Matches(combined)
				.Select(match => match.Value)
				.FirstOrDefault(IsRecoveredBatch)
				?? "NA";
			if (batch == "NA" && TryExtractPlausibleBatch(combined, out string recoveredBatch))
			{
				batch = recoveredBatch;
			}

			string itemName = NormalizeMedicineName(StripRecoveredFields(line));
			if (string.IsNullOrWhiteSpace(itemName) || !LooksLikeMedicineProduct(itemName))
			{
				string fallback = NormalizeMedicineName(line);
				itemName = LooksLikeMedicineProduct(fallback) ? fallback : string.Empty;
			}

			if (string.IsNullOrWhiteSpace(itemName) || !LooksLikeMedicineProduct(itemName))
			{
				continue;
			}

			List<Match> dates = SingleExpiryPattern.Matches(combined).Cast<Match>().ToList();
			string expiry = dates.Count == 0
				? string.Empty
				: FormatMonthYear(dates[^1].Groups[1].Value, dates[^1].Groups[2].Value);

			string numericText = DosageStrengthPattern.Replace(combined, " ");
			numericText = SingleExpiryPattern.Replace(numericText, " ");
			numericText = LooseBatchPattern.Replace(numericText, " ");
			List<decimal> numbers = MoneyTokenPattern.Matches(numericText)
				.Select(match => decimal.Parse(match.Value.Replace(",", string.Empty), CultureInfo.InvariantCulture))
				.Where(value => value > 0m)
				.ToList();
			decimal quantity = numbers.FirstOrDefault(value => value == decimal.Truncate(value) && value <= 5000m);
			if (quantity <= 0m)
			{
				quantity = 1m;
			}

			List<decimal> rates = numbers
				.Where(value => value != decimal.Truncate(value) || value >= 10m)
				.Where(value => value != quantity)
				.ToList();
			decimal rate = rates.ElementAtOrDefault(0);
			decimal amount = rates.ElementAtOrDefault(1);
			if (amount <= 0m && rate > 0m)
			{
				amount = decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
			}

			string key = itemName.Trim();
			if (!seen.Add(key))
			{
				continue;
			}

			yield return new ExtractedPurchaseLine(itemName, batch, expiry, quantity, 0m, 0m, rate, gst, amount);
		}
	}

	private static bool IsRecoveredBatch(string token)
	{
		string normalized = token.Trim();
		return normalized.Any(char.IsLetter)
			&& normalized.Any(char.IsDigit)
			&& !Regex.IsMatch(normalized, @"^(?:TABLETS?|CAPSULES?|SYRUP|PARACETAMOL|AMOXICILLIN|PANTOPRAZOLE|CETIRIZINE|AZITHROMYCIN|METFORMIN|BATCH|MFG|EXP|RATE|AMOUNT|QTY|GST|INVOICE)$", RegexOptions.IgnoreCase);
	}

	private static string StripRecoveredFields(string line)
	{
		string result = LooseBatchPattern.Replace(line, " ");
		result = SingleExpiryPattern.Replace(result, " ");
		result = DosageStrengthPattern.Replace(result, " ");
		result = Regex.Replace(result, @"\b(?:qty|quantity|rate|amount|mrp|free|gst)\b", " ", RegexOptions.IgnoreCase);
		return Regex.Replace(result, @"\s+", " ").Trim(" -:|".ToCharArray());
	}

	private static readonly Regex ForgivingBatchPattern = new Regex(@"\b([A-Za-z]{2,4}\d{4,6}[A-Za-z0-9]*)\b", RegexOptions.Compiled);

	private static readonly Regex ForgivingDatePattern = new Regex(@"\b(\d{2})[/-](\d{2,4})\b", RegexOptions.Compiled);

	private static readonly Regex ForgivingDecimalPattern = new Regex(@"\b\d+\.\d{1,2}\b", RegexOptions.Compiled);

	private static readonly Regex ForgivingIntegerPattern = new Regex(@"\b\d{1,5}\b", RegexOptions.Compiled);

	/// <summary>
	/// Ultra-forgiving pass: 3+ letters + any number qualifies. Missing batch/expiry get safe defaults.
	/// </summary>
	private static IEnumerable<ExtractedPurchaseLine> ParseForgivingLines(string[] lines, decimal gst)
	{
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string raw in lines)
		{
			string line = raw.Trim();
			if (line.Length < 4 || IsJunkForgivingLine(line) || !LooksLikeMedicineProduct(line))
			{
				continue;
			}

			int letters = line.Count(char.IsLetter);
			if (letters < 3)
			{
				continue;
			}

			_ = TryExtractPlausibleBatch(line, out string batch);
			if (string.IsNullOrWhiteSpace(batch))
			{
				batch = "NA";
			}

			string expiry = string.Empty;
			MatchCollection dates = ForgivingDatePattern.Matches(line);
			if (dates.Count > 0)
			{
				Match last = dates[^1];
				expiry = FormatMonthYear(last.Groups[1].Value, last.Groups[2].Value);
			}
			else
			{
				expiry = ExtractExpiry(line);
			}

			string moneyZone = StripDosageStrengths(line);
			moneyZone = ForgivingDatePattern.Replace(moneyZone, " ");
			moneyZone = ForgivingBatchPattern.Replace(moneyZone, " ");
			// Strip Form 20.8-style licence numbers so they are never read as Rate.
			moneyZone = Regex.Replace(moneyZone, @"form\s*\d+(?:\.\d+)?", " ", RegexOptions.IgnoreCase);

			List<decimal> decimals = ForgivingDecimalPattern.Matches(moneyZone)
				.Select(match => decimal.Parse(match.Value, CultureInfo.InvariantCulture))
				.ToList();
			List<int> integers = ForgivingIntegerPattern.Matches(moneyZone)
				.Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
				.Where(value => value > 0 && value < 100000)
				.ToList();

			// Integers left in the product name after stripping mg/ml are not Qty.
			decimal quantity = 1m;
			decimal rate = decimals.Count > 0 ? decimals[0] : 0m;
			decimal amount = decimals.Count > 1 ? decimals[^1] : 0m;
			decimal mrp = decimals.Count > 2 ? decimals[1] : 0m;
			if (integers.Count > 0 && decimals.Count >= 1)
			{
				// Qty is the integer that is not a leftover strength and sits with money columns.
				quantity = integers.FirstOrDefault(value => value >= 1 && value <= 5000 && (rate <= 0m || Math.Abs(value * rate - (amount > 0m ? amount : value * rate)) < 1m || amount <= 0m));
				if (quantity <= 0m)
				{
					quantity = integers[0] <= 5000 ? integers[0] : 1m;
				}
			}
			else if (integers.Count >= 2 && decimals.Count == 0)
			{
				quantity = integers[0];
				rate = integers[1];
			}

			if (amount <= 0m && rate > 0m)
			{
				amount = decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
			}

			if (rate > 0m && amount > 0m)
			{
				decimal derived = decimal.Round(amount / rate, 0, MidpointRounding.AwayFromZero);
				if (derived >= 1m && derived <= 5000m
					&& (quantity <= 1m || Math.Abs(quantity * rate - amount) > 1m))
				{
					quantity = derived;
				}
			}

			string nameZone = line;
			Match batchMatch = ForgivingBatchPattern.Match(line);
			if (batchMatch.Success && !string.Equals(batch, "NA", StringComparison.OrdinalIgnoreCase))
			{
				nameZone = line[..batchMatch.Index].Trim();
			}

			nameZone = ForgivingDatePattern.Replace(nameZone, " ");
			nameZone = ForgivingDecimalPattern.Replace(nameZone, " ");
			nameZone = Regex.Replace(nameZone, @"^\d{1,3}\s+", string.Empty).Trim();
			nameZone = Regex.Replace(nameZone, @"\s+\d+\s*[xX×]\s*\d+\s*$", string.Empty).Trim();
			nameZone = StripTrailingCompany(nameZone);
			nameZone = Regex.Replace(nameZone, @"\s{2,}", " ").Trim();
			if (!LooksLikeMedicineProduct(nameZone) || IsManufacturer(nameZone) || IsNonMedicineNoise(nameZone))
			{
				continue;
			}

			string key = nameZone + "|" + batch + "|" + quantity.ToString(CultureInfo.InvariantCulture);
			if (!seen.Add(key))
			{
				continue;
			}

			yield return new ExtractedPurchaseLine(nameZone, batch, expiry, quantity, 0m, mrp, rate, gst, amount);
		}
	}

	private static bool IsJunkForgivingLine(string line) =>
		IsColumnHeader(line) || IsTableEndMarker(line) || IsNonMedicineNoise(line);

	private static bool IsAcceptableMedicineLine(string name) =>
		!string.IsNullOrWhiteSpace(name)
		&& !IsNonMedicineNoise(name)
		&& LooksLikeMedicineProduct(name);

	/// <summary>
	/// Header / footer / licence noise that must never become an inward medicine row.
	/// </summary>
	public static bool IsNonMedicineNoise(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return true;
		}

		return Regex.IsMatch(
			line,
			@"drug\s*(?:no\.?|number|#|licen[cs]e|license)|form\s*20|form\s*21|\bgstin\b|\bcstin\b|"
			+ @"\bseller\b|\bbuyer\b|\bwholesaler\b|\bretailer\b|tax\s*invoice|cash[-\s]?credit|\bmemo\b|"
			+ @"dummy\s*invoice|training|demo\s*only|jurisdiction|authorised\s*signat|authorized\s*signat|"
			+ @"taxable\s*value|\bcgst\b|\bsgst\b|\bigst\b|grand\s*total|amount\s*in\s*words|"
			+ @"terms\s*&?\s*conditions|for\s+office\s+use|not\s+a\s+tax\s+document|mode\s*:\s*credit|page\s+\d+|"
			+ @"^product$|^manufacturer$|^pack$|^batch(\s*no\.?)?$|^mfg\s*/?\s*exp$|^rate(\s*\(rs\.?\))?$|^qty$|^amount$|"
			+ @"lal\s+chowk|srinagar|dummy[.\-]?(?:ws|rt)",
			RegexOptions.IgnoreCase);
	}

	/// <summary>True when the text looks like a real product (dosage form / strength) rather than an address or licence.</summary>
	public static bool LooksLikeMedicineProduct(string line)
	{
		if (string.IsNullOrWhiteSpace(line) || IsNonMedicineNoise(line))
		{
			return false;
		}

		string trimmed = line.Trim();

		// Reject standalone form / schedule crumbs: "Tablets", "Tablets (Sch. H)", "Cap (H1)".
		if (Regex.IsMatch(
			trimmed,
			@"^(?:tablets?|tabs?|capsules?|caps?|syrup|suspension|injection|inj\.?|ointment|cream|drops?|sachet|gel)"
			+ @"(?:\s*\([^)]*\))?$",
			RegexOptions.IgnoreCase))
		{
			return false;
		}

		// Strength-only leftovers: "5m 125 mg", "500 mg".
		if (Regex.IsMatch(trimmed, @"^(?:\d+\s*[a-z]{0,2}\s+)?\d+(?:\.\d+)?\s*(?:mg|mcg|ml|gm|iu)\b", RegexOptions.IgnoreCase)
			&& trimmed.Count(char.IsLetter) < 8)
		{
			return false;
		}

		// Require a dosage form word, OR a substantial name plus a strength unit.
		if (Regex.IsMatch(
			trimmed,
			@"\b(tablets?|tabs?|capsules?|caps?|syrup|suspension|injection|inj\.?|ointment|cream|drops?|sachet|gel|hydrochloride|amoxicillin|paracetamol|pantoprazole|cetirizine|azithromycin|metformin)\b",
			RegexOptions.IgnoreCase))
		{
			return true;
		}

		int letters = trimmed.Count(char.IsLetter);
		return letters >= 8
			&& Regex.IsMatch(trimmed, @"\b(mg|mcg|ml|gm|iu)\b", RegexOptions.IgnoreCase);
	}

	/// <summary>True for distributor batch codes like PCT2407 / AMC2512.</summary>
	public static bool HasPlausibleBatchToken(string? batch)
	{
		if (string.IsNullOrWhiteSpace(batch)
			|| string.Equals(batch, "NA", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(batch, "BATCH", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		return Regex.IsMatch(batch.Trim(), @"^[A-Za-z]{2,4}\d{4,6}[A-Za-z0-9]*$", RegexOptions.IgnoreCase)
			&& !batch.Contains("DUMMY", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryExtractPlausibleBatch(string line, out string batch)
	{
		batch = string.Empty;
		// Never treat Form 20-B / licence tokens as a batch.
		if (IsNonMedicineNoise(line) || Regex.IsMatch(line, @"form\s*20|form\s*21|drug\s*(?:no|licen)", RegexOptions.IgnoreCase))
		{
			return false;
		}

		Match match = ForgivingBatchPattern.Match(line);
		if (!match.Success)
		{
			return false;
		}

		string value = match.Groups[1].Value;
		if (value.Contains("DUMMY", StringComparison.OrdinalIgnoreCase)
			|| Regex.IsMatch(value, @"^(FORM|WS|RT|DL)\d", RegexOptions.IgnoreCase))
		{
			return false;
		}

		batch = value;
		return true;
	}

	/// <summary>
	/// When OCR dumps Product / Batch / Exp / Rate as separate vertical columns, zip them by index.
	/// </summary>
	private static IEnumerable<ExtractedPurchaseLine> TryParseColumnarTable(string[] tableLines, decimal gst)
	{
		List<string> products = new List<string>();
		List<string> batches = new List<string>();
		List<string> expiries = new List<string>();
		List<decimal> rates = new List<decimal>();
		List<decimal> amounts = new List<decimal>();
		List<decimal> quantities = new List<decimal>();

		string section = "product";
		bool seenProduct = false;
		foreach (string raw in tableLines)
		{
			string line = raw.Trim();
			if (line.Length == 0)
			{
				continue;
			}

			// Column headers must switch section even when they also match the noise blocklist.
			if (Regex.IsMatch(line, @"^product$", RegexOptions.IgnoreCase))
			{
				section = "product";
				continue;
			}

			if (Regex.IsMatch(line, @"^batch(\s*no\.?)?$|^batch\s*nd$", RegexOptions.IgnoreCase))
			{
				section = "batch";
				continue;
			}

			if (Regex.IsMatch(line, @"^(mfg\s*/?\s*exp|exp(?:iry)?)$", RegexOptions.IgnoreCase))
			{
				section = "exp";
				continue;
			}

			if (Regex.IsMatch(line, @"^rate(\s*\(rs\.?\))?$", RegexOptions.IgnoreCase))
			{
				// OCR often prints "Rate" before the product list — ignore until products exist.
				section = seenProduct ? "rate" : "product";
				continue;
			}

			if (Regex.IsMatch(line, @"^qty$|^quantity$", RegexOptions.IgnoreCase))
			{
				section = "qty";
				continue;
			}

			if (Regex.IsMatch(line, @"^amount(\s*\(rs\.?\))?$", RegexOptions.IgnoreCase))
			{
				section = "amount";
				continue;
			}

			if (Regex.IsMatch(line, @"^pack$|^manufacturer$|^si\.?$|^sl\.?$", RegexOptions.IgnoreCase))
			{
				section = "skip";
				continue;
			}

			if (IsNonMedicineNoise(line) || IsTableEndMarker(line))
			{
				continue;
			}

			if (section == "skip")
			{
				if (IsManufacturer(line) || PackPattern.IsMatch(line))
				{
					continue;
				}

				// After manufacturer/pack columns, the next real tokens are batches (or more products).
				section = seenProduct ? "batch" : "product";
			}

			// Prefer alphanumeric batch codes over money (PCT2407 must not become qty 2407).
			if (TryExtractPlausibleBatch(line, out string onlyBatch) && line.Count(char.IsLetter) >= 2
				&& line.Trim().Length <= onlyBatch.Length + 2)
			{
				batches.Add(onlyBatch);
				section = "batch";
				continue;
			}

			if (RangePattern.IsMatch(line) || (ForgivingDatePattern.IsMatch(line) && line.Count(char.IsLetter) < 3))
			{
				expiries.Add(ExtractExpiry(line));
				section = "exp";
				continue;
			}

			if (line.Any(char.IsLetter))
			{
				// Do not parse lettered tokens (batch leftovers, pack words) as money.
			}
			else if (TryParseMoneyToken(line, out decimal money))
			{
				bool whole = money == decimal.Truncate(money);

				// Qty sits after Mfg/Exp: whole numbers only, never dosage-sized leftovers from the name.
				if (section is "qty" or "exp"
					&& whole
					&& money is >= 1m and <= 5000m
					&& quantities.Count < Math.Max(products.Count, 1))
				{
					quantities.Add(money);
					section = "qty";
					continue;
				}

				if (section == "amount" || (rates.Count >= products.Count && products.Count > 0) || money >= 400m)
				{
					amounts.Add(money);
					section = "amount";
				}
				else if (section is "rate" or "qty" || seenProduct)
				{
					// Skip lone junk integers (e.g. "4") that appear before real rate decimals.
					if (!whole || money >= 10m || rates.Count > 0)
					{
						rates.Add(money);
						section = "rate";
					}
				}

				continue;
			}

			if (section is "product" or "skip" || (!seenProduct && line.Count(char.IsLetter) >= 3)
				|| (products.Count <= batches.Count && line.Count(char.IsLetter) >= 3))
			{
				if (IsManufacturer(line) || IsColumnHeader(line) || IsNonMedicineNoise(line) || !LooksLikeMedicineProduct(line))
				{
					continue;
				}

				string name = Regex.Replace(line, @"^\d{1,2}\s+", string.Empty).Trim();
				name = StripTrailingCompany(name);
				if (TryExtractPlausibleBatch(name, out string embeddedBatch)
					&& string.Equals(name.Trim(), embeddedBatch, StringComparison.OrdinalIgnoreCase))
				{
					batches.Add(embeddedBatch);
					section = "batch";
					continue;
				}

				if (TryExtractPlausibleBatch(name, out embeddedBatch))
				{
					name = NormalizeMedicineName(name);
					if (batches.Count < products.Count + 1)
					{
						batches.Add(embeddedBatch);
					}
				}
				else
				{
					name = NormalizeMedicineName(name);
				}

				if (LooksLikeMedicineProduct(name))
				{
					products.Add(name);
					seenProduct = true;
					section = "product";
				}
			}
		}

		int count = products.Count;
		if (count == 0)
		{
			yield break;
		}

		for (int i = 0; i < count; i++)
		{
			string batch = i < batches.Count ? batches[i] : "NA";
			string expiry = i < expiries.Count ? expiries[i] : string.Empty;
			decimal rate = i < rates.Count ? rates[i] : 0m;
			decimal amount = i < amounts.Count ? amounts[i] : 0m;
			decimal qty = i < quantities.Count ? quantities[i] : 0m;

			// Prefer Amount ÷ Rate when OCR dropped or misaligned the Qty column.
			if (rate > 0m && amount > 0m)
			{
				decimal derived = decimal.Round(amount / rate, 0, MidpointRounding.AwayFromZero);
				if (derived >= 1m && derived <= 5000m
					&& (qty <= 0m || Math.Abs(qty * rate - amount) > Math.Abs(derived * rate - amount) + 0.5m))
				{
					qty = derived;
				}
			}

			if (qty <= 0m)
			{
				qty = 1m;
			}

			if (amount <= 0m && rate > 0m)
			{
				amount = decimal.Round(qty * rate, 2, MidpointRounding.AwayFromZero);
			}

			yield return new ExtractedPurchaseLine(products[i], batch, expiry, qty, 0m, 0m, rate, gst, amount);
		}
	}

	private static bool TryParseMoneyToken(string line, out decimal value)
	{
		value = 0m;
		string text = line.Trim()
			.Replace("Rs.", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("Rs", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace(",", string.Empty)
			.Replace(" ", string.Empty);
		text = Regex.Replace(text, @"[^\d.]", string.Empty);
		return text.Length > 0 && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0m;
	}

	/// <summary>Prefer Exp label, else the second date in a Mfg/Exp composite, else a single MM/YY.</summary>
	public static string ExtractExpiry(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		Match labeled = LabeledExpPattern.Match(text);
		if (labeled.Success)
		{
			return FormatMonthYear(labeled.Groups[1].Value, labeled.Groups[2].Value);
		}

		Match range = RangePattern.Match(text);
		if (range.Success)
		{
			return FormatMonthYear(range.Groups[3].Value, range.Groups[4].Value);
		}

		// OCR often joins Mfg/Exp with a bullet (07/26 • 06/27) — take the last MM/YY.
		MatchCollection dates = ForgivingDatePattern.Matches(text);
		if (dates.Count >= 2)
		{
			Match last = dates[^1];
			return FormatMonthYear(last.Groups[1].Value, last.Groups[2].Value);
		}

		Match single = SingleExpiryPattern.Match(text);
		if (single.Success)
		{
			return FormatMonthYear(single.Groups[1].Value, single.Groups[2].Value);
		}

		return string.Empty;
	}

	/// <summary>Remove dosage strengths so 500 mg / 125 mg are never parsed as Qty.</summary>
	public static string StripDosageStrengths(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		string cleaned = DosageStrengthPattern.Replace(text, " ");
		// Bare trailing IP strength without unit (e.g. "IP 500" / "IP SOO") stays in the name.
		cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
		return cleaned;
	}

	private static bool LooksLikeColumnarInvoice(string[] tableLines)
	{
		int productLines = 0;
		int batchOnly = 0;
		int dateOnly = 0;
		bool hasColumnHeader = false;
		foreach (string raw in tableLines)
		{
			string line = raw.Trim();
			if (line.Length == 0)
			{
				continue;
			}

			if (Regex.IsMatch(line, @"^(product|manufacturer|pack|batch(\s*no\.?)?|mfg\s*/?\s*exp|exp(?:iry)?|rate|qty|quantity|amount)$", RegexOptions.IgnoreCase))
			{
				hasColumnHeader = true;
			}

			if (TryExtractPlausibleBatch(line, out string batch) && line.Trim().Length <= batch.Length + 2)
			{
				batchOnly++;
				continue;
			}

			if ((RangePattern.IsMatch(line) || ForgivingDatePattern.IsMatch(line)) && line.Count(char.IsLetter) < 3)
			{
				dateOnly++;
				continue;
			}

			if (LooksLikeMedicineProduct(line) && !TryExtractPlausibleBatch(line, out _) && !HasCompositeOrExpiryDate(line))
			{
				productLines++;
			}
		}

		return (hasColumnHeader && productLines >= 2)
			|| (productLines >= 3 && (batchOnly >= 2 || dateOnly >= 2));
	}

	private static bool TryReadItem(string[] lines, ref int cursor, decimal gst, out ExtractedPurchaseLine? item, out decimal discountPercent)
	{
		item = null;
		discountPercent = 0m;
		string line = lines[cursor];
		Match range = RangePattern.Match(line);
		if (range.Success && line.Length > range.Length + 8)
		{
			if (TryParseWideRow(line, range, gst, out item))
			{
				cursor++;
				return true;
			}
		}
		else if (range.Success && IsMostlyDateRange(line, range))
		{
			int start = cursor;
			while (start > 0 && !IsTableBoundary(lines[start - 1]) && !TryMoneyLine(lines[start - 1], out _))
			{
				start--;
			}

			List<string> cells = lines[start..cursor].Where(cell => !IsColumnHeader(cell)).ToList();
			if (TrySplitCells(cells, out string name, out string batch))
			{
				string expiry = FormatMonthYear(range.Groups[3].Value, range.Groups[4].Value);
				int after = cursor + 1;
				List<decimal> numbers = new List<decimal>();
				while (after < lines.Length && numbers.Count < 3 && TryMoneyLine(lines[after], out decimal money))
				{
					numbers.Add(money);
					after++;
				}

				if (numbers.Count >= 2)
				{
					decimal quantity = numbers[0];
					decimal rate = numbers[1];
					decimal amount = numbers.Count > 2 ? numbers[2] : decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
					item = new ExtractedPurchaseLine(name, batch, expiry, quantity, 0m, 0m, rate, gst, amount);
					cursor = after;
					return true;
				}
			}
		}

		if (IsNonMedicineNoise(line))
		{
			return false;
		}

		// Wide row with labeled Exp, slash delimiter, or missing expiry — keep the medicine if qty/rate exist.
		if (TryParseWideRowFlexible(line, gst, out item))
		{
			cursor++;
			return true;
		}

		return false;
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
		string moneyAfter = StripDosageStrengths(after);
		moneyAfter = DosageStrengthPattern.Replace(moneyAfter, " ");
		List<decimal> numbers = MoneyTokenPattern.Matches(moneyAfter)
			.Select(match => decimal.Parse(match.Value.Replace(",", string.Empty), CultureInfo.InvariantCulture))
			.Where(value => value > 0m)
			.ToList();
		if (head.Length < 2 || numbers.Count < 2)
		{
			return false;
		}

		decimal quantity = numbers[0] == decimal.Truncate(numbers[0]) ? numbers[0] : 1m;
		decimal rate = numbers[0] == decimal.Truncate(numbers[0]) ? numbers[1] : numbers[0];
		if (numbers[0] != decimal.Truncate(numbers[0]))
		{
			quantity = 1m;
			rate = numbers[0];
		}

		decimal amount = numbers.Count > 2 ? numbers[^1] : decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
		if (rate > 0m && amount > 0m)
		{
			decimal derived = decimal.Round(amount / rate, 0, MidpointRounding.AwayFromZero);
			if (derived >= 1m && derived <= 5000m && Math.Abs(quantity * rate - amount) > 1m)
			{
				quantity = derived;
			}
		}

		string expiry = FormatMonthYear(range.Groups[3].Value, range.Groups[4].Value);
		item = new ExtractedPurchaseLine(head, batch, expiry, quantity, 0m, 0m, rate, gst, amount);
		return true;
	}

	private static bool TryParseWideRowFlexible(string line, decimal gst, out ExtractedPurchaseLine? item)
	{
		item = null;
		if (IsColumnHeader(line) || IsNonMedicineNoise(line) || line.Length < 8 || !LooksLikeMedicineProduct(line))
		{
			return false;
		}

		_ = TryExtractPlausibleBatch(line, out string batch);
		if (string.IsNullOrWhiteSpace(batch))
		{
			batch = "NA";
		}

		Match batchMatch = BatchTokenPattern.Match(line);
		string before = batchMatch.Success && !string.Equals(batch, "NA", StringComparison.OrdinalIgnoreCase)
			? line[..batchMatch.Index].Trim()
			: line;
		string after = batchMatch.Success && !string.Equals(batch, "NA", StringComparison.OrdinalIgnoreCase)
			? line[(batchMatch.Index + batchMatch.Length)..]
			: line;
		string head = Regex.Replace(before, @"\s+\d+\s*[xX×]\s*\d+(?:\s+\S+){0,3}\s*$", string.Empty).Trim();
		head = StripTrailingCompany(head);
		head = Regex.Replace(head, @"^\d{1,2}\s+", string.Empty).Trim();
		head = ForgivingDatePattern.Replace(head, " ").Trim();
		head = Regex.Replace(head, @"\s{2,}", " ").Trim();
		if (!LooksLikeMedicineProduct(head) || IsManufacturer(head) || IsNonMedicineNoise(head))
		{
			return false;
		}

		string expiry = ExtractExpiry(line);
		// Strip date tokens and dosage strengths so 500 mg / 07/25 are never Qty.
		string moneySegment = StripDosageStrengths(after);
		moneySegment = RangePattern.Replace(moneySegment, " ");
		moneySegment = LabeledExpPattern.Replace(moneySegment, " ");
		moneySegment = SingleExpiryPattern.Replace(moneySegment, " ");
		moneySegment = ForgivingDatePattern.Replace(moneySegment, " ");
		moneySegment = ForgivingBatchPattern.Replace(moneySegment, " ");
		// Prefer the numeric tail after the product name (post Mfg/Exp), not strengths inside the name.
		string headWithoutStrength = StripDosageStrengths(head);
		if (!string.Equals(batch, "NA", StringComparison.OrdinalIgnoreCase) && batchMatch.Success)
		{
			moneySegment = StripDosageStrengths(after);
			moneySegment = RangePattern.Replace(moneySegment, " ");
			moneySegment = ForgivingDatePattern.Replace(moneySegment, " ");
		}
		else
		{
			// No batch on the line: only keep money tokens that are not residual name digits.
			moneySegment = Regex.Replace(moneySegment, Regex.Escape(headWithoutStrength), " ", RegexOptions.IgnoreCase);
		}

		List<decimal> numbers = MoneyTokenPattern.Matches(moneySegment)
			.Select(match => decimal.Parse(match.Value.Replace(",", string.Empty), CultureInfo.InvariantCulture))
			.Where(value => value > 0m)
			.ToList();
		if (numbers.Count < 1)
		{
			// Product-only OCR line (columnar remnant) — keep the medicine with Qty 1; rates filled later or by user.
			item = new ExtractedPurchaseLine(head, batch, expiry, 1m, 0m, 0m, 0m, gst, 0m);
			return true;
		}

		decimal quantity = 1m;
		decimal rate = 0m;
		decimal amount = 0m;
		List<decimal> wholes = numbers.Where(value => value == decimal.Truncate(value) && value < 10000m).ToList();
		List<decimal> decimals = numbers.Where(value => value != decimal.Truncate(value) || value >= 10000m).ToList();
		if (decimals.Count >= 2)
		{
			rate = decimals[0];
			amount = decimals[^1];
			if (wholes.Count > 0)
			{
				quantity = wholes[0];
			}
			else if (rate > 0m)
			{
				quantity = decimal.Round(amount / rate, 0, MidpointRounding.AwayFromZero);
			}
		}
		else if (decimals.Count == 1 && wholes.Count >= 1)
		{
			quantity = wholes[0];
			rate = decimals[0];
			amount = numbers.Count >= 3 ? numbers[^1] : decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
		}
		else if (wholes.Count >= 2)
		{
			quantity = wholes[0];
			rate = wholes[1];
			amount = wholes.Count >= 3 ? wholes[2] : decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
		}
		else
		{
			rate = numbers[0];
			quantity = 1m;
			amount = rate;
		}

		if (rate > 0m && amount > 0m)
		{
			decimal derived = decimal.Round(amount / rate, 0, MidpointRounding.AwayFromZero);
			if (derived >= 1m && derived <= 5000m && Math.Abs(quantity * rate - amount) > 1m)
			{
				quantity = derived;
			}
		}

		if (amount <= 0m && rate > 0m)
		{
			amount = decimal.Round(quantity * rate, 2, MidpointRounding.AwayFromZero);
		}

		item = new ExtractedPurchaseLine(head, batch, expiry, quantity, 0m, 0m, rate, gst, amount);
		return true;
	}

	private static bool TryReadLooseItem(string[] lines, ref int cursor, decimal gst, out ExtractedPurchaseLine? item)
	{
		item = null;
		string line = lines[cursor];
		if (!TryParseWideRowFlexible(line, gst, out item) || item == null)
		{
			return false;
		}

		cursor++;
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
		if (RangePattern.IsMatch(line) || IsColumnHeader(line) || IsTableEndMarker(line))
		{
			return true;
		}

		return false;
	}

	private static bool IsTableEndMarker(string line) =>
		Regex.IsMatch(
			line,
			@"taxable\s*value|amount\s*in\s*words|\bcgst\b|\bsgst\b|\bigst\b|grand\s*total|declaration|terms\s*&?\s*conditions|jurisdiction|authorised\s*signat|authorized\s*signat",
			RegexOptions.IgnoreCase);

	private static bool IsColumnHeader(string line)
	{
		string trimmed = line.Trim();
		bool looksLikeHeaderRow =
			(trimmed.Contains("product", StringComparison.OrdinalIgnoreCase)
				|| trimmed.Contains("medicine", StringComparison.OrdinalIgnoreCase)
				|| trimmed.Contains("description", StringComparison.OrdinalIgnoreCase))
			&& (trimmed.Contains("batch", StringComparison.OrdinalIgnoreCase)
				|| trimmed.Contains("qty", StringComparison.OrdinalIgnoreCase)
				|| trimmed.Contains("rate", StringComparison.OrdinalIgnoreCase));

		return looksLikeHeaderRow
			|| trimmed.Equals("batch", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("batch no.", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("batch no", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("mfg / exp", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("mfg/exp", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("product", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("qty", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("rate", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("amount", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("manufacturer", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("pack", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("si", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("sl", StringComparison.OrdinalIgnoreCase)
			|| trimmed.Equals("s.no", StringComparison.OrdinalIgnoreCase);
	}

	private static int FindTableStart(string[] lines)
	{
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			bool hasProduct = line.Contains("product", StringComparison.OrdinalIgnoreCase)
				|| line.Contains("medicine", StringComparison.OrdinalIgnoreCase)
				|| line.Contains("description", StringComparison.OrdinalIgnoreCase);
			bool hasBatch = line.Contains("batch", StringComparison.OrdinalIgnoreCase);
			bool hasExp = line.Contains("mfg", StringComparison.OrdinalIgnoreCase) || line.Contains("exp", StringComparison.OrdinalIgnoreCase);
			bool hasSi = Regex.IsMatch(line, @"\b(si|sl|s\.?\s*no)\b", RegexOptions.IgnoreCase);
			if ((hasProduct && (hasBatch || hasExp)) || (hasBatch && hasExp) || (hasSi && hasProduct && hasBatch))
			{
				return index + 1;
			}
		}

		// Fallback: first medicine-like line after invoice header labels.
		for (int index = 0; index < lines.Length; index++)
		{
			if (IsNonMedicineNoise(lines[index]) || IsColumnHeader(lines[index]))
			{
				continue;
			}

			if (lines[index].Count(char.IsLetter) >= 6
				&& ForgivingBatchPattern.IsMatch(lines[index])
				&& HasCompositeOrExpiryDate(lines[index]))
			{
				return index;
			}
		}

		return 0;
	}

	private static int FindTableEnd(string[] lines, int tableStart)
	{
		for (int index = Math.Max(tableStart, 0); index < lines.Length; index++)
		{
			if (IsTableEndMarker(lines[index]))
			{
				return index;
			}
		}

		return lines.Length;
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
		// Prefer Seller (Wholesaler) — never the Buyer (Retailer) box.
		for (int index = 0; index < lines.Length; index++)
		{
			bool sellerAnchor = Regex.IsMatch(lines[index], @"\bseller\b|\bwholesaler\b", RegexOptions.IgnoreCase)
				&& !Regex.IsMatch(lines[index], @"\bbuyer\b|\bretailer\b", RegexOptions.IgnoreCase);
			if (!sellerAnchor)
			{
				continue;
			}

			for (int next = index + 1; next < Math.Min(lines.Length, index + 6); next++)
			{
				if (Regex.IsMatch(lines[next], @"\bbuyer\b|\bretailer\b|tax\s*invoice|invoice\s*n|cash[-\s]?credit", RegexOptions.IgnoreCase))
				{
					break;
				}

				if (IsNonMedicineNoise(lines[next])
					|| Regex.IsMatch(lines[next], @"^\d|road|chowk|srinagar|street|colony|nagar", RegexOptions.IgnoreCase))
				{
					continue;
				}

				string? cleaned = CleanSupplier(NormalizeSupplierPrefix(lines[next]));
				if (!string.IsNullOrWhiteSpace(cleaned)
					&& !GeminiPurchaseImportService.LooksLikeFileOrDeviceId(cleaned)
					&& !LooksLikeBuyerSupplierName(cleaned))
				{
					return EnsureMsPrefix(cleaned);
				}
			}
		}

		bool inBuyerBlock = false;
		for (int index = 0; index < Math.Min(lines.Length, 40); index++)
		{
			if (Regex.IsMatch(lines[index], @"\bbuyer\b|\bretailer\b", RegexOptions.IgnoreCase))
			{
				inBuyerBlock = true;
				continue;
			}

			if (Regex.IsMatch(lines[index], @"\bseller\b|\bwholesaler\b", RegexOptions.IgnoreCase)
				&& !Regex.IsMatch(lines[index], @"\bbuyer\b", RegexOptions.IgnoreCase))
			{
				inBuyerBlock = false;
			}

			if (inBuyerBlock)
			{
				continue;
			}

			string candidate = NormalizeSupplierPrefix(lines[index]);
			if ((candidate.Contains("M/s", StringComparison.OrdinalIgnoreCase)
					|| candidate.Contains("Pharma", StringComparison.OrdinalIgnoreCase)
					|| candidate.Contains("Distribut", StringComparison.OrdinalIgnoreCase))
				&& !candidate.Contains("buyer", StringComparison.OrdinalIgnoreCase))
			{
				string? cleaned = CleanSupplier(candidate);
				if (!string.IsNullOrWhiteSpace(cleaned)
					&& !GeminiPurchaseImportService.LooksLikeFileOrDeviceId(cleaned)
					&& !LooksLikeBuyerSupplierName(cleaned))
				{
					return EnsureMsPrefix(cleaned);
				}
			}
		}

		return null;
	}

	private static string NormalizeSupplierPrefix(string line)
	{
		string text = (line ?? string.Empty).Trim();
		// OCR often turns "M/s" into "Ws" / "W/s".
		text = Regex.Replace(text, @"^(?:W/s|Ws|M\\s|Ms)\.?\s+", "M/s ", RegexOptions.IgnoreCase);
		return text;
	}

	private static string EnsureMsPrefix(string name)
	{
		string trimmed = name.Trim();
		return trimmed.StartsWith("M/s", StringComparison.OrdinalIgnoreCase) ? trimmed : "M/s " + trimmed;
	}

	private static bool LooksLikeBuyerSupplierName(string name) =>
		Regex.IsMatch(name, @"medical\s*hall|sample\s*hall|\bbuyer\b|\bretailer\b", RegexOptions.IgnoreCase);

	private static string? CleanSupplier(string line)
	{
		if (string.IsNullOrWhiteSpace(line) || IsNonMedicineNoise(line) || GeminiPurchaseImportService.LooksLikeFileOrDeviceId(line))
		{
			return null;
		}

		if (LooksLikeBuyerSupplierName(line))
		{
			return null;
		}

		Match named = Regex.Match(line, @"M/s\.?\s*(.+?)(?=\s+M/s\.?\s+|\s+Date\s*:|\s+Invoice\s*No|$)", RegexOptions.IgnoreCase);
		string name = named.Success
			? "M/s " + named.Groups[1].Value.Trim()
			: line.Trim();
		int date = name.IndexOf("Date", StringComparison.OrdinalIgnoreCase);
		if (date > 0)
		{
			name = name[..date].Trim();
		}

		name = Regex.Replace(name, @"\s{2,}", " ").Trim();
		if (name.Count(char.IsLetter) < 4)
		{
			return null;
		}

		return name.Length > 100 ? name[..100] : name;
	}

	private static string? MatchInvoiceNo(string[] lines)
	{
		for (int index = 0; index < lines.Length; index++)
		{
			Match match = InvoiceNoPattern.Match(lines[index]);
			if (match.Success)
			{
				string value = match.Groups[1].Value.Trim().TrimEnd('.', ',', ';');
				if (!GeminiPurchaseImportService.LooksLikeFileOrDeviceId(value) && value.Length >= 3)
				{
					return value;
				}
			}

			// Label on its own line; value on the next line (common OCR split).
			if (Regex.IsMatch(lines[index], @"^(invoice|bill)\s*(no\.?|number|#)\s*[:\-]?$", RegexOptions.IgnoreCase)
				&& index + 1 < lines.Length)
			{
				string next = lines[index + 1].Trim().TrimEnd('.', ',', ';');
				if (Regex.IsMatch(next, @"^[A-Z0-9][A-Z0-9/\-]{2,}$", RegexOptions.IgnoreCase)
					&& !GeminiPurchaseImportService.LooksLikeFileOrDeviceId(next))
				{
					return next;
				}
			}
		}

		return null;
	}

	private static string GuessDate(string[] lines)
	{
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			Match match = LabeledDatePattern.Match(line);
			if (!match.Success && (line.Contains("invoice", StringComparison.OrdinalIgnoreCase) || line.Contains("bill no", StringComparison.OrdinalIgnoreCase)))
			{
				match = LooseDatePattern.Match(line);
			}

			if (!match.Success
				&& Regex.IsMatch(line, @"^(invoice|bill)\s*(no\.?|number|#)", RegexOptions.IgnoreCase)
				&& index + 1 < lines.Length)
			{
				match = LooseDatePattern.Match(lines[index + 1]);
			}

			if (!match.Success && Regex.IsMatch(line, @"^date\s*[:\-]?", RegexOptions.IgnoreCase))
			{
				match = LooseDatePattern.Match(line);
				if (!match.Success && index + 1 < lines.Length)
				{
					match = LooseDatePattern.Match(lines[index + 1]);
				}
			}

			if (!match.Success)
			{
				continue;
			}

			if (!TryBuildIsoDate(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, out string iso))
			{
				continue;
			}

			return iso;
		}

		return DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
	}

	private static bool TryBuildIsoDate(string a, string b, string c, out string iso)
	{
		iso = string.Empty;
		int first = int.Parse(a, CultureInfo.InvariantCulture);
		int second = int.Parse(b, CultureInfo.InvariantCulture);
		int year = int.Parse(c, CultureInfo.InvariantCulture);
		if (c.Length == 2)
		{
			year += 2000;
		}

		// Prefer DD-MM-YYYY (Indian invoices); fall back to MM-DD-YYYY when day would be invalid.
		int day = first;
		int month = second;
		if (month is < 1 or > 12 && first is >= 1 and <= 12)
		{
			month = first;
			day = second;
		}

		if (month is < 1 or > 12 || day is < 1 or > 31 || year is < 2000 or > 2100)
		{
			return false;
		}

		day = Math.Min(day, DateTime.DaysInMonth(year, month));
		iso = new DateOnly(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
		return true;
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
