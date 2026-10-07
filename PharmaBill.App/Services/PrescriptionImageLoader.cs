using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PharmaBill.App.Services;

public static class PrescriptionImageLoader
{
	private const long MaximumFileBytes = 15728640L;

	public static async Task<LoadedPrescriptionImage> LoadAsync(string path)
	{
		FileInfo fileInfo = new FileInfo(path);
		if (!fileInfo.Exists)
		{
			throw new FileNotFoundException("The prescription file could not be found.", path);
		}
		if (fileInfo.Length == 0L || fileInfo.Length > 15728640)
		{
			throw new InvalidOperationException("The prescription file must be between 1 byte and 15 MB.");
		}
		switch (fileInfo.Extension.ToLowerInvariant())
		{
		case ".png":
			return new LoadedPrescriptionImage(await File.ReadAllBytesAsync(path), "image/png");
		case ".jpg":
		case ".jpeg":
			return new LoadedPrescriptionImage(await File.ReadAllBytesAsync(path), "image/jpeg");
		case ".bmp":
			return new LoadedPrescriptionImage(ConvertBitmapToPng(await File.ReadAllBytesAsync(path)), "image/png");
		case ".pdf":
			return new LoadedPrescriptionImage(await RenderFirstPdfPageAsync(path), "image/png");
		default:
			throw new InvalidOperationException("Choose a JPG, PNG, BMP or PDF prescription.");
		}
	}

	private static byte[] ConvertBitmapToPng(byte[] bytes)
	{
		BitmapDecoder bitmapDecoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(bitmapDecoder.Frames[0]);
		using MemoryStream memoryStream = new MemoryStream();
		pngBitmapEncoder.Save(memoryStream);
		return memoryStream.ToArray();
	}

	private static async Task<byte[]> RenderFirstPdfPageAsync(string path)
	{
		PdfDocument pdfDocument = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path)));
		if (pdfDocument.PageCount == 0)
		{
			throw new InvalidOperationException("The PDF has no pages.");
		}
		using PdfPage page = pdfDocument.GetPage(0u);
		using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
		await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
		{
			DestinationWidth = 2000u
		});
		stream.Seek(0uL);
		using DataReader reader = new DataReader(stream.GetInputStreamAt(0uL));
		await reader.LoadAsync((uint)stream.Size);
		byte[] array = new byte[stream.Size];
		reader.ReadBytes(array);
		return array;
	}
}
