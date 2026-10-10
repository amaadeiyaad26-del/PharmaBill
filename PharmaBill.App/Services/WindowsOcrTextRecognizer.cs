using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Core.Ai;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PharmaBill.App.Services;

public sealed class WindowsOcrTextRecognizer : IOcrTextRecognizer
{
	public async Task<string> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default(CancellationToken))
	{
		OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("Windows OCR is not available: install a Windows language pack with text recognition.");
		using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
		using (DataWriter writer = new DataWriter(stream))
		{
			writer.WriteBytes(imageBytes);
			await writer.StoreAsync();
			writer.DetachStream();
		}
		stream.Seek(0uL);
		BitmapDecoder bitmapDecoder = await BitmapDecoder.CreateAsync(stream);
		uint maxImageDimension = OcrEngine.MaxImageDimension;
		double num = Math.Min(1.0, (double)maxImageDimension / (double)Math.Max(bitmapDecoder.PixelWidth, bitmapDecoder.PixelHeight));
		BitmapTransform transform = new BitmapTransform
		{
			ScaledWidth = (uint)Math.Max(1.0, (double)bitmapDecoder.PixelWidth * num),
			ScaledHeight = (uint)Math.Max(1.0, (double)bitmapDecoder.PixelHeight * num)
		};
		using SoftwareBitmap source = await bitmapDecoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
		cancellationToken.ThrowIfCancellationRequested();
		using SoftwareBitmap enhanced = EnhanceForOcr(source);
		return string.Join('\n', (await engine.RecognizeAsync(enhanced)).Lines.Select((OcrLine line) => line.Text));
	}

	public async Task<IReadOnlyList<OcrWordPosition>> RecognizeWordsAsync(byte[] imageBytes, CancellationToken cancellationToken = default(CancellationToken))
	{
		OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("Windows OCR is not available: install a Windows language pack with text recognition.");
		using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
		using (DataWriter writer = new DataWriter(stream))
		{
			writer.WriteBytes(imageBytes);
			await writer.StoreAsync();
			writer.DetachStream();
		}

		stream.Seek(0uL);
		BitmapDecoder bitmapDecoder = await BitmapDecoder.CreateAsync(stream);
		uint maxImageDimension = OcrEngine.MaxImageDimension;
		double scale = Math.Min(1.0, (double)maxImageDimension / Math.Max(bitmapDecoder.PixelWidth, bitmapDecoder.PixelHeight));
		BitmapTransform transform = new BitmapTransform
		{
			ScaledWidth = (uint)Math.Max(1.0, bitmapDecoder.PixelWidth * scale),
			ScaledHeight = (uint)Math.Max(1.0, bitmapDecoder.PixelHeight * scale)
		};
		using SoftwareBitmap source = await bitmapDecoder.GetSoftwareBitmapAsync(
			BitmapPixelFormat.Bgra8,
			BitmapAlphaMode.Premultiplied,
			transform,
			ExifOrientationMode.RespectExifOrientation,
			ColorManagementMode.DoNotColorManage);
		cancellationToken.ThrowIfCancellationRequested();
		using SoftwareBitmap enhanced = EnhanceForOcr(source);
		Windows.Media.Ocr.OcrResult result = await engine.RecognizeAsync(enhanced);
		return result.Lines
			.SelectMany(line => line.Words)
			.Select(word => new OcrWordPosition(
				word.Text,
				word.BoundingRect.X,
				word.BoundingRect.Y,
				word.BoundingRect.Width,
				word.BoundingRect.Height))
			.ToList();
	}

	/// <summary>Grayscale + contrast stretch so faint printed invoices OCR more reliably.</summary>
	private static SoftwareBitmap EnhanceForOcr(SoftwareBitmap source)
	{
		SoftwareBitmap working = source.BitmapPixelFormat == BitmapPixelFormat.Bgra8
			? SoftwareBitmap.Copy(source)
			: SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8);
		byte[] pixels = new byte[4 * working.PixelWidth * working.PixelHeight];
		working.CopyToBuffer(pixels.AsBuffer());

		byte min = 255;
		byte max = 0;
		for (int i = 0; i < pixels.Length; i += 4)
		{
			byte gray = (byte)((pixels[i] * 28 + pixels[i + 1] * 150 + pixels[i + 2] * 78) / 256);
			if (gray < min)
			{
				min = gray;
			}

			if (gray > max)
			{
				max = gray;
			}
		}

		int span = Math.Max(1, max - min);
		for (int i = 0; i < pixels.Length; i += 4)
		{
			int gray = (pixels[i] * 28 + pixels[i + 1] * 150 + pixels[i + 2] * 78) / 256;
			byte stretched = (byte)Math.Clamp((gray - min) * 255 / span, 0, 255);
			pixels[i] = stretched;
			pixels[i + 1] = stretched;
			pixels[i + 2] = stretched;
		}

		working.CopyFromBuffer(pixels.AsBuffer());
		return working;
	}
}
