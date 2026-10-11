using System.IO;
using System.Windows.Media.Imaging;
using PharmaBill.Core.Labels;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace PharmaBill.App.Services;

public sealed class BarcodeLabelPdfService
{
	public byte[] Build(BarcodeLabelPlan plan, string title, int columns = 3)
	{
		ArgumentNullException.ThrowIfNull(plan);
		if (columns <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(columns));
		}
		Dictionary<string, byte[]> barcodes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		foreach (BarcodeLabelCell cell in plan.Cells)
		{
			if (!barcodes.ContainsKey(cell.Code))
			{
				barcodes[cell.Code] = Code128Png(cell.Code);
			}
		}

		return Document.Create(document =>
		{
			document.Page(page =>
			{
				page.Size(PageSizes.A4);
				page.Margin(12);
				page.DefaultTextStyle(text => text.FontFamily(PdfFonts.Family).FontSize(8));
				page.Header().PaddingBottom(6).Text(title).FontSize(12).SemiBold();
				page.Content().Table(table =>
				{
					table.ColumnsDefinition(definition =>
					{
						for (int i = 0; i < columns; i++)
						{
							definition.RelativeColumn();
						}
					});

					foreach (BarcodeLabelCell cell in plan.Cells)
					{
						table.Cell().Padding(3).Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(5).Column(column =>
						{
							column.Spacing(2);
							column.Item().Text(cell.Title).SemiBold().FontSize(9).ClampLines(2);
							if (cell.Subtitle.Length > 0)
							{
								column.Item().Text(cell.Subtitle).FontColor(Colors.Grey.Darken1);
							}
							column.Item().Height(30).Image(barcodes[cell.Code]).FitArea();
							column.Item().AlignCenter().Text(cell.Code).FontSize(7);
							column.Item().Row(row =>
							{
								row.RelativeItem().Text(cell.Price).SemiBold();
								row.RelativeItem().AlignRight().Text(cell.Expiry);
							});
							if (cell.Batch.Length > 0)
							{
								column.Item().Text(cell.Batch).FontSize(7);
							}
						});
					}

					// Fill the last row so the grid keeps its shape.
					int remainder = plan.Cells.Count % columns;
					if (remainder != 0)
					{
						for (int i = 0; i < columns - remainder; i++)
						{
							table.Cell();
						}
					}
				});
			});
		}).GeneratePdf();
	}

	private static byte[] Code128Png(string code)
	{
		PixelData pixelData = new BarcodeWriterPixelData
		{
			Format = BarcodeFormat.CODE_128,
			Options = new EncodingOptions
			{
				Width = 400,
				Height = 120,
				Margin = 0,
				PureBarcode = true
			}
		}.Write(code);
		BitmapSource bitmap = BitmapSource.Create(pixelData.Width, pixelData.Height, 96.0, 96.0, System.Windows.Media.PixelFormats.Bgra32, null, pixelData.Pixels, pixelData.Width * 4);
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(bitmap));
		using MemoryStream stream = new MemoryStream();
		encoder.Save(stream);
		return stream.ToArray();
	}
}
