using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PharmaBill.App.Controls;

namespace RenderBrandIcon;

/// <summary>
/// Renders <see cref="PharmaBill3DLogo"/> via RenderTargetBitmap into PNG + multi-size ICO.
/// </summary>
internal static class Program
{
	[STAThread]
	private static int Main(string[] args)
	{
		var appRoot = FindAppRoot();
		var icons = Path.Combine(appRoot, "Assets", "Icons");
		Directory.CreateDirectory(icons);

		// Headless WPF application context for measure/arrange/render.
		_ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

		var resources = Path.Combine(appRoot, "Resources");
		Directory.CreateDirectory(resources);

		// Locked source-of-truth raster (512², clean alpha) + icon kit.
		var pngFinal = RenderLogoPng(512, showShadow: true);
		var png256 = RenderLogoPng(256, showShadow: true);
		var pngWatermark = RenderLogoPng(512, showShadow: false);

		var finalPath = Path.Combine(resources, "logo_final_3d.png");
		File.WriteAllBytes(finalPath, pngFinal);
		Console.WriteLine($"PNG {finalPath} ({pngFinal.Length:N0} bytes)");

		File.WriteAllBytes(Path.Combine(icons, "app_icon_256.png"), png256);
		File.WriteAllBytes(Path.Combine(icons, "pharma_bill_3d.png"), png256);
		File.WriteAllBytes(Path.Combine(icons, "pharma_bill_3d_watermark.png"), pngWatermark);
		File.WriteAllBytes(Path.Combine(icons, "logo_watermark.png"), pngWatermark);

		var icoBytes = BuildPngIco(
			RenderLogoBitmap(256, showShadow: true),
			RenderLogoBitmap(128, showShadow: true),
			RenderLogoBitmap(64, showShadow: true),
			RenderLogoBitmap(48, showShadow: true),
			RenderLogoBitmap(32, showShadow: true),
			RenderLogoBitmap(16, showShadow: false));

		foreach (var target in new[]
		{
			Path.Combine(appRoot, "app.ico"),
			Path.Combine(icons, "app.ico"),
			Path.Combine(icons, "pharma_bill_3d.ico"),
			Path.Combine(appRoot, "Assets", "PharmaBill.ico"),
			Path.Combine(Directory.GetParent(appRoot)!.FullName, "app.ico"),
		})
		{
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.WriteAllBytes(target, icoBytes);
			Console.WriteLine($"ICO {target} ({icoBytes.Length:N0} bytes)");
		}

		Console.WriteLine("Done — vector logo baked into app.ico + PNG watermarks.");
		return 0;
	}

	private static string FindAppRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null)
		{
			var candidate = Path.Combine(dir.FullName, "PharmaBill.App");
			if (File.Exists(Path.Combine(candidate, "PharmaBill.App.csproj")))
				return candidate;
			if (File.Exists(Path.Combine(dir.FullName, "PharmaBill.App.csproj")))
				return dir.FullName;
			dir = dir.Parent;
		}
		throw new InvalidOperationException("Could not locate PharmaBill.App project root.");
	}

	private static PharmaBill3DLogo CreateLogo(int size, bool showShadow)
	{
		var logo = new PharmaBill3DLogo
		{
			Width = size,
			Height = size,
			ShowAmbientShadow = showShadow,
			Background = Brushes.Transparent,
			ClipToBounds = false,
		};
		logo.Measure(new Size(size, size));
		logo.Arrange(new Rect(0, 0, size, size));
		logo.UpdateLayout();
		return logo;
	}

	private static RenderTargetBitmap RenderLogoBitmap(int size, bool showShadow)
	{
		var logo = CreateLogo(size, showShadow);
		var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
		rtb.Render(logo);
		rtb.Freeze();
		return rtb;
	}

	private static byte[] RenderLogoPng(int size, bool showShadow)
	{
		var rtb = RenderLogoBitmap(size, showShadow);
		var encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(rtb));
		using var ms = new MemoryStream();
		encoder.Save(ms);
		return ms.ToArray();
	}

	private static byte[] BuildPngIco(params RenderTargetBitmap[] bitmaps)
	{
		var ordered = bitmaps.OrderByDescending(b => b.PixelWidth).ToArray();
		var payloads = new List<byte[]>(ordered.Length);
		foreach (var bmp in ordered)
		{
			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(bmp));
			using var ms = new MemoryStream();
			encoder.Save(ms);
			payloads.Add(ms.ToArray());
		}

		using var output = new MemoryStream();
		using var bw = new BinaryWriter(output);
		bw.Write((ushort)0); // reserved
		bw.Write((ushort)1); // icon
		bw.Write((ushort)ordered.Length);
		var offset = 6 + (16 * ordered.Length);
		for (var i = 0; i < ordered.Length; i++)
		{
			var w = ordered[i].PixelWidth;
			var h = ordered[i].PixelHeight;
			bw.Write((byte)(w >= 256 ? 0 : w));
			bw.Write((byte)(h >= 256 ? 0 : h));
			bw.Write((byte)0); // colors
			bw.Write((byte)0); // reserved
			bw.Write((ushort)1); // planes
			bw.Write((ushort)32); // bit count
			bw.Write(payloads[i].Length);
			bw.Write(offset);
			offset += payloads[i].Length;
		}
		foreach (var payload in payloads)
			bw.Write(payload);
		return output.ToArray();
	}
}
