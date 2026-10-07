using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaBill.App.Services;

public sealed class TabularExportService
{
	public async Task ExportAsync(string destinationPath, string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath, "destinationPath");
		switch (Path.GetExtension(destinationPath).ToLowerInvariant())
		{
		case ".csv":
			await ExportCsvAsync(destinationPath, columns, rows, cancellationToken);
			break;
		case ".xlsx":
			await Task.Run(() =>
			{
				ExportExcel(destinationPath, title, columns, rows);
			}, cancellationToken);
			break;
		case ".pdf":
			await Task.Run(() =>
			{
				ExportPdf(destinationPath, title, columns, rows);
			}, cancellationToken);
			break;
		default:
			throw new ArgumentException("Choose a PDF, Excel (.xlsx), or CSV destination.", "destinationPath");
		}
	}

	private static async Task ExportCsvAsync(string path, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows, CancellationToken cancellationToken)
	{
		StringBuilder stringBuilder = new StringBuilder();
		AppendCsvRow(stringBuilder, columns);
		foreach (IReadOnlyList<string> row in rows)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AppendCsvRow(stringBuilder, row);
		}
		await File.WriteAllTextAsync(path, stringBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
	}

	private static void ExportExcel(string path, string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
	{
		using XLWorkbook xLWorkbook = new XLWorkbook();
		IXLWorksheet iXLWorksheet = xLWorkbook.Worksheets.Add((title.Length > 31) ? title.Substring(0, 31) : title);
		for (int i = 0; i < columns.Count; i++)
		{
			iXLWorksheet.Cell(1, i + 1).Value = columns[i];
			iXLWorksheet.Cell(1, i + 1).Style.Font.Bold = true;
		}
		for (int j = 0; j < rows.Count; j++)
		{
			for (int k = 0; k < columns.Count; k++)
			{
				iXLWorksheet.Cell(j + 2, k + 1).Value = ((k < rows[j].Count) ? rows[j][k] : string.Empty);
			}
		}
		iXLWorksheet.Columns().AdjustToContents();
		xLWorkbook.SaveAs(path);
	}

	private static void ExportPdf(string path, string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
	{
		Document.Create((IDocumentContainer document) =>
		{
			document.Page((PageDescriptor page) =>
			{
				page.Size(PageSizes.A4.Landscape());
				page.Margin(24f);
				page.DefaultTextStyle((TextStyle style) => style.FontFamily(PdfFonts.Family).FontSize(8f));
				page.Header().Text(title).FontSize(16f)
					.Bold();
				page.Content().PaddingVertical(12f).Table((TableDescriptor table) =>
				{
					table.ColumnsDefinition((TableColumnsDefinitionDescriptor definition) =>
					{
						foreach (string column in columns)
						{
							_ = column;
							definition.RelativeColumn();
						}
					});
					table.Header((TableCellDescriptor header) =>
					{
						foreach (string column2 in columns)
						{
							header.Cell().Background(Colors.Grey.Lighten2).Padding(4f)
								.Text(column2)
								.Bold();
						}
					});
					foreach (IReadOnlyList<string> row in rows)
					{
						for (int num = 0; num < columns.Count; num++)
						{
							table.Cell().BorderBottom(1f).BorderColor(Colors.Grey.Lighten2)
								.Padding(4f)
								.Text((num < row.Count) ? row[num] : string.Empty);
						}
					}
				});
				page.Footer().AlignRight().Text($"Generated {DateTime.Now:dd-MMM-yyyy HH:mm}");
			});
		}).GeneratePdf(path);
	}

	private static void AppendCsvRow(StringBuilder builder, IEnumerable<string> values)
	{
		builder.AppendLine(string.Join(',', values.Select((string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")));
	}
}
