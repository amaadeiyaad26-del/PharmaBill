using System.IO;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PharmaBill.App.Services;

/// <summary>Renders a PDF's pages to images with Windows' own PDF engine, for printing and for the built-in viewers.</summary>
public static class PdfPageRenderer
{
	public static async Task<IReadOnlyList<BitmapSource>> RenderAsync(string pdfPath, double dpi)
	{
		StorageFile file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(pdfPath));
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
}
