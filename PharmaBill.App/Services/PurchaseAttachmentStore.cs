using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media.Imaging;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PharmaBill.App.Services;

/// <summary>Copies supplier invoices into local app storage and builds a safe preview.</summary>
public static class PurchaseAttachmentStore
{
	public static bool IsPdf(string? path) =>
		string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

	public static string Copy(string sourcePath, int year, Guid supplierId)
	{
		string folder = FolderFor(year, supplierId);
		Directory.CreateDirectory(folder);
		string original = Path.GetFileName(sourcePath);
		string safe = string.Join("_", original.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
		if (string.IsNullOrWhiteSpace(safe))
		{
			safe = "invoice" + Path.GetExtension(sourcePath);
		}

		string destination = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + safe);
		if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
		{
			return destination;
		}

		File.Copy(sourcePath, destination, overwrite: false);
		return destination;
	}

	public static string Relocate(string currentPath, int year, Guid supplierId)
	{
		if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
		{
			return currentPath;
		}

		string folder = FolderFor(year, supplierId);
		Directory.CreateDirectory(folder);
		if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(currentPath) ?? string.Empty), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
		{
			return currentPath;
		}

		string destination = Path.Combine(folder, Path.GetFileName(currentPath));
		if (File.Exists(destination))
		{
			destination = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Path.GetFileName(currentPath));
		}

		File.Move(currentPath, destination);
		return destination;
	}

	public static BitmapImage? LoadImage(string path, int decodeWidth)
	{
		if (IsPdf(path) || !File.Exists(path))
		{
			return null;
		}

		BitmapImage image = new BitmapImage();
		image.BeginInit();
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.UriSource = new Uri(path);
		image.DecodePixelWidth = decodeWidth;
		image.EndInit();
		image.Freeze();
		return image;
	}

	public static async Task<BitmapImage?> RenderPdfFirstPageAsync(string path)
	{
		if (!IsPdf(path) || !File.Exists(path))
		{
			return null;
		}

		try
		{
			StorageFile file = await StorageFile.GetFileFromPathAsync(path);
			PdfDocument pdf = await PdfDocument.LoadFromFileAsync(file);
			if (pdf.PageCount == 0)
			{
				return null;
			}

			using PdfPage page = pdf.GetPage(0);
			using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
			await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = 480 });
			stream.Seek(0);
			using Stream net = stream.AsStreamForRead();
			MemoryStream memory = new MemoryStream();
			await net.CopyToAsync(memory);
			memory.Position = 0;
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = memory;
			image.EndInit();
			image.Freeze();
			return image;
		}
		catch
		{
			return null;
		}
	}

	public static string? PathFromLegacyNotes(string? notes)
	{
		if (string.IsNullOrWhiteSpace(notes))
		{
			return null;
		}

		const string marker = "Invoice image:";
		int index = notes.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
		if (index < 0)
		{
			return null;
		}

		string path = notes[(index + marker.Length)..].Trim();
		int lineBreak = path.IndexOfAny(new[] { '\r', '\n' });
		if (lineBreak >= 0)
		{
			path = path[..lineBreak].Trim();
		}

		return path.Length == 0 ? null : path;
	}

	private static string FolderFor(int year, Guid supplierId)
	{
		string supplier = supplierId == Guid.Empty ? "unassigned" : supplierId.ToString("N");
		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"PharmaBill",
			"Attachments",
			"Purchases",
			year.ToString(CultureInfo.InvariantCulture),
			supplier);
	}
}
