using System.IO;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace PharmaBill.App.Services;

public sealed class TabularExportService
{
    public async Task ExportAsync(
        string destinationPath,
        string title,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var extension = Path.GetExtension(destinationPath).ToLowerInvariant();
        switch (extension)
        {
            case ".csv":
                await ExportCsvAsync(destinationPath, columns, rows, cancellationToken);
                break;
            case ".xlsx":
                await Task.Run(() => ExportExcel(destinationPath, title, columns, rows), cancellationToken);
                break;
            case ".pdf":
                await Task.Run(() => ExportPdf(destinationPath, title, columns, rows), cancellationToken);
                break;
            default:
                throw new ArgumentException("Choose a PDF, Excel (.xlsx), or CSV destination.", nameof(destinationPath));
        }
    }

    private static async Task ExportCsvAsync(
        string path,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder, columns);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendCsvRow(builder, row);
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
    }

    private static void ExportExcel(
        string path,
        string title,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add(title.Length > 31 ? title[..31] : title);
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            worksheet.Cell(1, columnIndex + 1).Value = columns[columnIndex];
            worksheet.Cell(1, columnIndex + 1).Style.Font.Bold = true;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                worksheet.Cell(rowIndex + 2, columnIndex + 1).Value =
                    columnIndex < rows[rowIndex].Count ? rows[rowIndex][columnIndex] : string.Empty;
            }
        }

        worksheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    private static void ExportPdf(
        string path,
        string title,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        Document.Create(document =>
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontFamily(PdfFonts.Family).FontSize(8));
                page.Header().Text(title).FontSize(16).Bold();
                page.Content().PaddingVertical(12).Table(table =>
                {
                    table.ColumnsDefinition(definition =>
                    {
                        foreach (var _ in columns)
                        {
                            definition.RelativeColumn();
                        }
                    });
                    table.Header(header =>
                    {
                        foreach (var column in columns)
                        {
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(column).Bold();
                        }
                    });
                    foreach (var row in rows)
                    {
                        for (var index = 0; index < columns.Count; index++)
                        {
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4)
                                .Text(index < row.Count ? row[index] : string.Empty);
                        }
                    }
                });
                page.Footer().AlignRight().Text($"Generated {DateTime.Now:dd-MMM-yyyy HH:mm}");
            })).GeneratePdf(path);
    }

    private static void AppendCsvRow(StringBuilder builder, IEnumerable<string> values)
    {
        builder.AppendLine(string.Join(',', values.Select(value =>
            $"\"{(value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\"")));
    }
}
