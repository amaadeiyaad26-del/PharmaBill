using System.IO;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace PharmaBill.App.Services;

public sealed record PurchaseSpreadsheetData(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Rows);

public sealed class PurchaseSpreadsheetReader
{
    public Task<PurchaseSpreadsheetData> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Read(filePath, cancellationToken), cancellationToken);

    private static PurchaseSpreadsheetData Read(string filePath, CancellationToken cancellationToken)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".csv" => ReadCsv(filePath, cancellationToken),
            ".xlsx" => ReadExcel(filePath, cancellationToken),
            _ => throw new InvalidOperationException("Choose a CSV or XLSX file.")
        };
    }

    private static PurchaseSpreadsheetData ReadCsv(string filePath, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(filePath, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            BadDataFound = args => throw new InvalidDataException($"Malformed CSV data: {args.Field}")
        });
        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is null || csv.HeaderRecord.Length == 0)
        {
            throw new InvalidDataException("The selected CSV does not contain a header row.");
        }

        var headers = csv.HeaderRecord.Select(header => header.Trim()).ToArray();
        ValidateHeaders(headers);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        while (csv.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                values[headers[index]] = csv.GetField(index)?.Trim() ?? string.Empty;
            }

            if (values.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                rows.Add(values);
            }
        }

        return new PurchaseSpreadsheetData(headers, rows);
    }

    private static PurchaseSpreadsheetData ReadExcel(string filePath, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(filePath);
        var range = workbook.Worksheets.FirstOrDefault()?.RangeUsed()
            ?? throw new InvalidDataException("The selected workbook is empty.");
        var firstRow = range.FirstRowUsed().RowNumber();
        var firstColumn = range.FirstColumnUsed().ColumnNumber();
        var lastColumn = range.LastColumnUsed().ColumnNumber();
        var headers = Enumerable.Range(firstColumn, lastColumn - firstColumn + 1)
            .Select(column => range.Worksheet.Cell(firstRow, column).GetString().Trim())
            .ToArray();
        ValidateHeaders(headers);
        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var row in range.RowsUsed().Where(row => row.RowNumber() > firstRow))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
            {
                values[headers[index]] = row.Cell(firstColumn + index).GetFormattedString().Trim();
            }

            if (values.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            {
                rows.Add(values);
            }
        }

        return new PurchaseSpreadsheetData(headers, rows);
    }

    private static void ValidateHeaders(IReadOnlyCollection<string> headers)
    {
        if (headers.Count == 0 ||
            headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count)
        {
            throw new InvalidDataException("The spreadsheet must have unique, non-empty column headers.");
        }
    }
}
