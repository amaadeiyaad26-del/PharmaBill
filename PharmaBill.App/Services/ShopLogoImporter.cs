using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace PharmaBill.App.Services;

/// <summary>
/// Turns an uploaded shop logo (PNG, JPG or SVG) into one normalised PNG, at most <see cref="MaxPixels"/> on its
/// longer side with transparency kept, so it can be stored with Firm Profile and drawn the same way everywhere.
/// Must run on a WPF (STA) thread.
/// </summary>
public static class ShopLogoImporter
{
	public const int MaxPixels = 384;

	public const long MaxFileBytes = 5L * 1024 * 1024;

	public const string DialogFilter = "Logo images (*.png;*.jpg;*.jpeg;*.svg)|*.png;*.jpg;*.jpeg;*.svg";

	public static byte[] ToPng(string path)
	{
		FileInfo file = new FileInfo(path);
		if (!file.Exists)
		{
			throw new InvalidOperationException("The logo file was not found.");
		}

		if (file.Length > MaxFileBytes)
		{
			throw new InvalidOperationException("The logo file is larger than 5 MB. Choose a smaller image.");
		}

		string extension = file.Extension.ToLowerInvariant();
		try
		{
			return extension switch
			{
				".svg" => RenderSvg(file.FullName),
				".png" or ".jpg" or ".jpeg" => RenderRaster(file.FullName),
				_ => throw new InvalidOperationException("Use a PNG, JPG or SVG image for the logo.")
			};
		}
		catch (InvalidOperationException)
		{
			throw;
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException("The logo could not be read. Save it again as PNG, JPG or SVG and retry.", ex);
		}
	}

	/// <summary>A frozen image for display, or null when there is no logo or it cannot be decoded.</summary>
	public static ImageSource? ToImageSource(byte[]? png, int decodeHeight = 0)
	{
		if (png is not { Length: > 0 })
		{
			return null;
		}

		try
		{
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.StreamSource = new MemoryStream(png);
			if (decodeHeight > 0)
			{
				image.DecodePixelHeight = decodeHeight;
			}

			image.EndInit();
			image.Freeze();
			return image;
		}
		catch (Exception)
		{
			return null;
		}
	}

	private static byte[] RenderRaster(string path)
	{
		using FileStream stream = File.OpenRead(path);
		BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
		BitmapSource frame = decoder.Frames[0];
		return Render(frame, new Rect(0, 0, frame.PixelWidth, frame.PixelHeight), (context, target) => context.DrawImage(frame, target));
	}

	private static byte[] RenderSvg(string path)
	{
		WpfDrawingSettings settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true };
		using FileSvgReader reader = new FileSvgReader(settings);
		DrawingGroup drawing = reader.Read(path) ?? throw new InvalidOperationException("The SVG logo is empty.");
		Rect bounds = drawing.Bounds;
		if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
		{
			throw new InvalidOperationException("The SVG logo has nothing to draw.");
		}

		return Render(drawing, bounds, (context, target) =>
		{
			context.PushTransform(new TranslateTransform(target.X, target.Y));
			context.PushTransform(new ScaleTransform(target.Width / bounds.Width, target.Height / bounds.Height));
			context.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));
			context.DrawDrawing(drawing);
			context.Pop();
			context.Pop();
			context.Pop();
		});
	}

	/// <summary>Draws the source scaled to fit <see cref="MaxPixels"/> (never enlarged) on a transparent canvas and encodes PNG.</summary>
	private static byte[] Render(object source, Rect bounds, Action<DrawingContext, Rect> draw)
	{
		double scale = Math.Min(1.0, MaxPixels / Math.Max(bounds.Width, bounds.Height));
		if (source is DrawingGroup)
		{
			// Vector art has no native pixel size: always render it at the full target size.
			scale = MaxPixels / Math.Max(bounds.Width, bounds.Height);
		}

		int width = Math.Max(1, (int)Math.Round(bounds.Width * scale));
		int height = Math.Max(1, (int)Math.Round(bounds.Height * scale));
		DrawingVisual visual = new DrawingVisual();
		RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
		using (DrawingContext context = visual.RenderOpen())
		{
			draw(context, new Rect(0, 0, width, height));
		}

		RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
		bitmap.Render(visual);
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(bitmap));
		using MemoryStream output = new MemoryStream();
		encoder.Save(output);
		return output.ToArray();
	}
}
