using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PharmaBill.App.Services;

/// <summary>Normalizes Android camera uploads (HEIC/WEBP/unknown) to JPEG for OCR and Gemini vision.</summary>
public static class InvoiceImageNormalizer
{
	public static async Task<string> EnsureSupportedAsync(string path, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return path;
		}

		string extension = Path.GetExtension(path).ToLowerInvariant();
		if (extension is ".pdf" or ".jpg" or ".jpeg" or ".png")
		{
			byte[] probe = await File.ReadAllBytesAsync(path, cancellationToken);
			if (LooksLikeJpeg(probe) || LooksLikePng(probe) || LooksLikePdf(probe) || extension == ".pdf")
			{
				return path;
			}
		}

		byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken);
		if (LooksLikeJpeg(bytes) || LooksLikePng(bytes) || LooksLikePdf(bytes))
		{
			return path;
		}

		string folder = Path.GetDirectoryName(path) ?? Path.GetTempPath();
		string jpegPath = Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + "-normalized.jpg");
		await EncodeJpegAsync(bytes, jpegPath, cancellationToken);
		return jpegPath;
	}

	private static async Task EncodeJpegAsync(byte[] imageBytes, string destination, CancellationToken cancellationToken)
	{
		using InMemoryRandomAccessStream input = new InMemoryRandomAccessStream();
		using (DataWriter writer = new DataWriter(input))
		{
			writer.WriteBytes(imageBytes);
			await writer.StoreAsync();
			writer.DetachStream();
		}

		input.Seek(0);
		BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input);
		cancellationToken.ThrowIfCancellationRequested();
		using SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(
			BitmapPixelFormat.Bgra8,
			BitmapAlphaMode.Premultiplied,
			new BitmapTransform(),
			ExifOrientationMode.RespectExifOrientation,
			ColorManagementMode.DoNotColorManage);

		using InMemoryRandomAccessStream output = new InMemoryRandomAccessStream();
		BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output);
		encoder.SetSoftwareBitmap(bitmap);
		encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
		await encoder.FlushAsync();
		output.Seek(0);
		using Stream netStream = output.AsStreamForRead();
		using MemoryStream buffer = new MemoryStream();
		await netStream.CopyToAsync(buffer, cancellationToken);
		await File.WriteAllBytesAsync(destination, buffer.ToArray(), cancellationToken);
	}

	private static bool LooksLikeJpeg(byte[] bytes) =>
		bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

	private static bool LooksLikePng(byte[] bytes) =>
		bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

	private static bool LooksLikePdf(byte[] bytes) =>
		bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46;
}
