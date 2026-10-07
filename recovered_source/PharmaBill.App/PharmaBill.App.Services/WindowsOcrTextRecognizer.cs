using System;
using System.Linq;
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
		using SoftwareBitmap bitmap = await bitmapDecoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
		cancellationToken.ThrowIfCancellationRequested();
		return string.Join('\n', (await engine.RecognizeAsync(bitmap)).Lines.Select((OcrLine line) => line.Text));
	}
}
