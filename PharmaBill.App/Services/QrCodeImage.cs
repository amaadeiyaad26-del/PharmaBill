using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;

namespace PharmaBill.App.Services;

public static class QrCodeImage
{
	public static BitmapSource? FromText(string? text, int size = 480)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		PixelData pixelData = new BarcodeWriterPixelData
		{
			Format = BarcodeFormat.QR_CODE,
			Options = new QrCodeEncodingOptions
			{
				Width = size,
				Height = size,
				Margin = 2,
				ErrorCorrection = ErrorCorrectionLevel.M,
				CharacterSet = "UTF-8"
			}
		}.Write(text);
		BitmapSource bitmapSource = BitmapSource.Create(pixelData.Width, pixelData.Height, 96.0, 96.0, PixelFormats.Bgra32, null, pixelData.Pixels, pixelData.Width * 4);
		bitmapSource.Freeze();
		return bitmapSource;
	}
}
