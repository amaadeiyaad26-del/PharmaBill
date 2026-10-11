using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace PharmaBill.App.Services;

public sealed class PurchaseSpreadsheetReader
{
	private static readonly string[] HeaderHints =
	[
		"medicine", "drug", "item", "product", "batch", "lot", "expiry", "exp",
		"qty", "quantity", "mrp", "rate", "ptr", "amount", "gst", "free"
	];

	public Task<PurchaseSpreadsheetData> ReadAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.Run(() => Read(filePath, cancellationToken), cancellationToken);
	}

	private static PurchaseSpreadsheetData Read(string filePath, CancellationToken cancellationToken)
	{
		string extension = Path.GetExtension(filePath).ToLowerInvariant();
		return extension switch
		{
			".csv" => ReadCsv(filePath, cancellationToken),
			".xlsx" => ReadExcel(filePath, cancellationToken),
			".xls" => throw new InvalidOperationException(
				"Excel 97-2003 (.xls) is not supported. Open the file in Excel and Save As → Excel Workbook (*.xlsx) or CSV (*.csv), then try again."),
			_ => throw new InvalidOperationException("Choose a CSV or XLSX file.")
		};
	}

	private static PurchaseSpreadsheetData ReadCsv(string filePath, CancellationToken cancellationToken)
	{
		using StreamReader reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		using CsvReader csvReader = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
		{
			DetectDelimiter = true,
			BadDataFound = null,
			MissingFieldFound = null,
			HeaderValidated = null,
			TrimOptions = TrimOptions.Trim
		});
		if (!csvReader.Read() || !csvReader.ReadHeader() || csvReader.HeaderRecord == null || csvReader.HeaderRecord.Length == 0)
		{
			throw new InvalidDataException("The selected CSV does not contain a header row.");
		}

		string[] headers = NormalizeHeaders(csvReader.HeaderRecord);
		List<IReadOnlyDictionary<string, string>> rows = new List<IReadOnlyDictionary<string, string>>();
		while (csvReader.Read())
		{
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (int index = 0; index < headers.Length; index++)
			{
				dictionary[headers[index]] = CleanCell(csvReader.GetField(index));
			}

			if (dictionary.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
			{
				rows.Add(dictionary);
			}
		}

		if (rows.Count == 0)
		{
			throw new InvalidDataException("The CSV has headers but no data rows.");
		}

		return new PurchaseSpreadsheetData(headers, rows);
	}

	private static PurchaseSpreadsheetData ReadExcel(string filePath, CancellationToken cancellationToken)
	{
		using XLWorkbook workbook = new XLWorkbook(filePath);
		IXLWorksheet? sheet = workbook.Worksheets.FirstOrDefault(ws => ws.RangeUsed() != null)
			?? throw new InvalidDataException("The selected workbook is empty.");
		IXLRange range = sheet.RangeUsed()
			?? throw new InvalidDataException("The selected workbook is empty.");

		int firstCol = range.FirstColumnUsed().ColumnNumber();
		int lastCol = range.LastColumnUsed().ColumnNumber();
		int firstRow = range.FirstRowUsed().RowNumber();
		int lastRow = range.LastRowUsed().RowNumber();

		int headerRow = FindHeaderRow(sheet, firstRow, lastRow, firstCol, lastCol);
		string[] rawHeaders = Enumerable.Range(firstCol, lastCol - firstCol + 1)
			.Select(column => sheet.Cell(headerRow, column).GetFormattedString())
			.ToArray();
		string[] headers = NormalizeHeaders(rawHeaders);

		List<IReadOnlyDictionary<string, string>> rows = new List<IReadOnlyDictionary<string, string>>();
		for (int rowNumber = headerRow + 1; rowNumber <= lastRow; rowNumber++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (int index = 0; index < headers.Length; index++)
			{
				dictionary[headers[index]] = CleanCell(sheet.Cell(rowNumber, firstCol + index).GetFormattedString());
			}

			if (dictionary.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
			{
				rows.Add(dictionary);
			}
		}

		if (rows.Count == 0)
		{
			throw new InvalidDataException("The spreadsheet has headers but no data rows.");
		}

		return new PurchaseSpreadsheetData(headers, rows);
	}

	private static int FindHeaderRow(IXLWorksheet sheet, int firstRow, int lastRow, int firstCol, int lastCol)
	{
		int scanTo = Math.Min(lastRow, firstRow + 15);
		int bestRow = firstRow;
		int bestScore = -1;
		for (int row = firstRow; row <= scanTo; row++)
		{
			int score = 0;
			int nonEmpty = 0;
			for (int col = firstCol; col <= lastCol; col++)
			{
				string cell = sheet.Cell(row, col).GetFormattedString().Trim();
				if (string.IsNullOrWhiteSpace(cell))
				{
					continue;
				}

				nonEmpty++;
				string normalized = NormalizeToken(cell);
				if (HeaderHints.Any(hint => normalized.Contains(hint, StringComparison.OrdinalIgnoreCase)))
				{
					score += 2;
				}
			}

			if (nonEmpty >= 3)
			{
				score += 1;
			}

			if (score > bestScore)
			{
				bestScore = score;
				bestRow = row;
			}
		}

		return bestRow;
	}

	private static string[] NormalizeHeaders(IReadOnlyList<string> raw)
	{
		List<string> headers = new List<string>(raw.Count);
		HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < raw.Count; index++)
		{
			string name = (raw[index] ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(name))
			{
				name = "Column" + (index + 1);
			}

			string unique = name;
			int suffix = 2;
			while (!used.Add(unique))
			{
				unique = name + " (" + suffix + ")";
				suffix++;
			}

			headers.Add(unique);
		}

		if (headers.Count == 0)
		{
			throw new InvalidDataException("The spreadsheet must have column headers.");
		}

		return headers.ToArray();
	}

	private static string CleanCell(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string trimmed = value.Trim().Trim('"');
		// Strip currency / noise often pasted from Excel exports.
		trimmed = trimmed.Replace("₹", string.Empty, StringComparison.Ordinal)
			.Replace("Rs.", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Replace("INR", string.Empty, StringComparison.OrdinalIgnoreCase)
			.Trim();
		return trimmed;
	}

	private static string NormalizeToken(string value) =>
		value.Replace(" ", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.Replace(".", string.Empty, StringComparison.Ordinal);
}
