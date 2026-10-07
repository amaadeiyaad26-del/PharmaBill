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
	public Task<PurchaseSpreadsheetData> ReadAsync(string filePath, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.Run(() => Read(filePath, cancellationToken), cancellationToken);
	}

	private static PurchaseSpreadsheetData Read(string filePath, CancellationToken cancellationToken)
	{
		string text = Path.GetExtension(filePath).ToLowerInvariant();
		if (!(text == ".csv"))
		{
			if (text == ".xlsx")
			{
				return ReadExcel(filePath, cancellationToken);
			}
			throw new InvalidOperationException("Choose a CSV or XLSX file.");
		}
		return ReadCsv(filePath, cancellationToken);
	}

	private static PurchaseSpreadsheetData ReadCsv(string filePath, CancellationToken cancellationToken)
	{
		using StreamReader reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
		using CsvReader csvReader = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
		{
			DetectDelimiter = true,
			BadDataFound = (BadDataFoundArgs args) =>
			{
				throw new InvalidDataException("Malformed CSV data: " + args.Field);
			}
		});
		if (!csvReader.Read() || !csvReader.ReadHeader() || csvReader.HeaderRecord == null || csvReader.HeaderRecord.Length == 0)
		{
			throw new InvalidDataException("The selected CSV does not contain a header row.");
		}
		string[] array = csvReader.HeaderRecord.Select((string header) => header.Trim()).ToArray();
		ValidateHeaders(array);
		List<IReadOnlyDictionary<string, string>> list = new List<IReadOnlyDictionary<string, string>>();
		while (csvReader.Read())
		{
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (int num = 0; num < array.Length; num++)
			{
				dictionary[array[num]] = csvReader.GetField(num)?.Trim() ?? string.Empty;
			}
			if (dictionary.Values.Any((string value) => !string.IsNullOrWhiteSpace(value)))
			{
				list.Add(dictionary);
			}
		}
		return new PurchaseSpreadsheetData(array, list);
	}

	private static PurchaseSpreadsheetData ReadExcel(string filePath, CancellationToken cancellationToken)
	{
		using XLWorkbook xLWorkbook = new XLWorkbook(filePath);
		IXLRange range = xLWorkbook.Worksheets.FirstOrDefault()?.RangeUsed() ?? throw new InvalidDataException("The selected workbook is empty.");
		int firstRow = range.FirstRowUsed().RowNumber();
		int num = range.FirstColumnUsed().ColumnNumber();
		int num2 = range.LastColumnUsed().ColumnNumber();
		string[] array = (from column in Enumerable.Range(num, num2 - num + 1)
			select range.Worksheet.Cell(firstRow, column).GetString().Trim()).ToArray();
		ValidateHeaders(array);
		List<IReadOnlyDictionary<string, string>> list = new List<IReadOnlyDictionary<string, string>>();
		foreach (IXLRangeRow item in from row in range.RowsUsed()
			where row.RowNumber() > firstRow
			select row)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (int num3 = 0; num3 < array.Length; num3++)
			{
				dictionary[array[num3]] = item.Cell(num + num3).GetFormattedString().Trim();
			}
			if (dictionary.Values.Any((string value) => !string.IsNullOrWhiteSpace(value)))
			{
				list.Add(dictionary);
			}
		}
		return new PurchaseSpreadsheetData(array, list);
	}

	private static void ValidateHeaders(IReadOnlyCollection<string> headers)
	{
		if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count)
		{
			throw new InvalidDataException("The spreadsheet must have unique, non-empty column headers.");
		}
	}
}
