using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PharmaBill.Data.Services;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PharmaBill.App.Services;

/// <summary>
/// The printable copy of an invoice opened from Reports: the same document the shop prints at the counter (the A4 tax
/// invoice for a retail bill, the wholesale invoice), as a PDF, and as page images for the Windows print dialog.
/// </summary>
// downloadsFolder: where Export saves; the Windows Downloads folder unless a test gives its own.
public sealed class InvoiceDocumentService(RetailBillPdfService retailPdf, WholesaleInvoiceDocumentService wholesaleDocuments, string? downloadsFolder = null)
{
	/// <summary>Print resolution for the page images: sharp text without huge spool files.</summary>
	private const double PrintDpi = 200;

	public async Task WritePdfAsync(InvoiceDetail detail, string destinationPath, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
		if (detail.IsWholesale)
		{
			string generated = await wholesaleDocuments.ExportAsync(detail.DocumentId, deliveryChallan: false, cancellationToken);
			File.Copy(generated, destinationPath, overwrite: true);
		}
		else
		{
			await retailPdf.ExportAsync(detail.DocumentId, destinationPath, RetailDocumentType.Invoice, cancellationToken);
		}
	}

	/// <summary>Saves straight to the Downloads folder, never over an earlier copy. Returns the file's path.</summary>
	public async Task<string> ExportToDownloadsAsync(InvoiceDetail detail, CancellationToken cancellationToken = default)
	{
		string folder = downloadsFolder ?? FolderBackupService.DownloadsFolder() ?? throw new InvalidOperationException("The Downloads folder could not be found. Use Save as PDF and choose a folder.");
		string path = UniquePath(folder, FileName(detail));
		await WritePdfAsync(detail, path, cancellationToken);
		return path;
	}

	/// <summary>"Retail bill BR01-INV-2026-27-000002.pdf": the invoice number with characters Windows forbids replaced.</summary>
	public static string FileName(InvoiceDetail detail)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		string name = new string($"{detail.DocumentType} {detail.InvoiceNo}".Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
		return name + ".pdf";
	}

	/// <summary>The invoice's pages as images, for the Windows print dialog. The PDF is the single source of the layout.</summary>
	public async Task<IReadOnlyList<BitmapSource>> RenderPagesAsync(InvoiceDetail detail, CancellationToken cancellationToken = default)
	{
		string pdf = Path.Combine(Path.GetTempPath(), $"PharmaRemit-print-{Guid.NewGuid():N}.pdf");
		try
		{
			await WritePdfAsync(detail, pdf, cancellationToken);
			return await RenderPdfPagesAsync(pdf, PrintDpi);
		}
		finally
		{
			try
			{
				File.Delete(pdf);
			}
			catch (IOException)
			{
				// Still open in the renderer; the temp folder is cleaned by Windows.
			}
		}
	}

	internal static async Task<IReadOnlyList<BitmapSource>> RenderPdfPagesAsync(string pdfPath, double dpi)
	{
		StorageFile file = await StorageFile.GetFileFromPathAsync(pdfPath);
		PdfDocument document = await PdfDocument.LoadFromFileAsync(file);
		var pages = new List<BitmapSource>((int)document.PageCount);
		for (uint index = 0; index < document.PageCount; index++)
		{
			using PdfPage page = document.GetPage(index);
			using var stream = new InMemoryRandomAccessStream();
			// PdfPage.Size is in device-independent pixels (1/96 inch).
			await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = (uint)Math.Round(page.Size.Width * dpi / 96) });
			stream.Seek(0);
			var memory = new MemoryStream();
			await stream.AsStreamForRead().CopyToAsync(memory);
			memory.Position = 0;
			var image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = memory;
			image.EndInit();
			image.Freeze();
			pages.Add(image);
		}
		return pages;
	}

	private static string UniquePath(string folder, string fileName)
	{
		string path = Path.Combine(folder, fileName);
		string stem = Path.GetFileNameWithoutExtension(fileName);
		for (int copy = 2; File.Exists(path); copy++)
		{
			path = Path.Combine(folder, $"{stem} ({copy}).pdf");
		}
		return path;
	}
}
