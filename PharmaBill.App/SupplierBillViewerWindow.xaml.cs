using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

/// <summary>Shows the supplier's own bill (PDF, scan or photo) attached to a purchase, page by page.</summary>
public partial class SupplierBillViewerWindow : Window
{
	public static readonly DependencyProperty PageWidthProperty = DependencyProperty.Register(nameof(PageWidth), typeof(double), typeof(SupplierBillViewerWindow), new PropertyMetadata(800d));

	private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff"];

	private readonly string _path;

	public SupplierBillViewerWindow(PurchaseBillAttachment bill, string path)
	{
		InitializeComponent();
		_path = path;
		TitleText.Text = $"Supplier bill {bill.BillNo}";
		SubtitleText.Text = $"{bill.Supplier} · {Path.GetFileName(path)}";
		Loaded += async (_, _) => await LoadAsync();
	}

	/// <summary>Displayed width of each page; Fit width and the zoom buttons change it.</summary>
	public double PageWidth
	{
		get => (double)GetValue(PageWidthProperty);
		set => SetValue(PageWidthProperty, value);
	}

	/// <summary>Opens the file in Windows' own viewer. Replaceable so a test can see the request.</summary>
	public Action<string> OpenExternally { get; set; } = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

	/// <summary>The pages on screen, once loaded.</summary>
	public IReadOnlyList<BitmapSource> LoadedPages { get; private set; } = Array.Empty<BitmapSource>();

	private async Task LoadAsync()
	{
		StatusText.Text = "Opening the bill…";
		try
		{
			string extension = Path.GetExtension(_path).ToLowerInvariant();
			if (extension == ".pdf")
			{
				LoadedPages = await PdfPageRenderer.RenderAsync(_path, 150);
			}
			else if (ImageExtensions.Contains(extension))
			{
				var image = new BitmapImage();
				image.BeginInit();
				image.CacheOption = BitmapCacheOption.OnLoad;
				image.UriSource = new Uri(Path.GetFullPath(_path));
				image.EndInit();
				image.Freeze();
				LoadedPages = [image];
			}
			else
			{
				StatusText.Text = $"This kind of file ({extension}) can't be shown here. Use Open in default viewer.";
				return;
			}
			Pages.ItemsSource = LoadedPages;
			FitWidth();
			StatusText.Text = LoadedPages.Count == 1 ? "1 page" : $"{LoadedPages.Count} pages";
		}
		catch (Exception exception)
		{
			StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18));
			StatusText.Text = "The bill could not be shown here: " + PharmaBill.Core.UserMessage.Of(exception) + " Use Open in default viewer.";
		}
	}

	private void FitWidth()
	{
		UpdateLayout();
		double available = PageScroller.ViewportWidth - 40;
		PageWidth = available > 200 ? available : 800;
	}

	private void FitWidth_Click(object sender, RoutedEventArgs e) => FitWidth();

	private void ZoomIn_Click(object sender, RoutedEventArgs e) => PageWidth = Math.Min(PageWidth * 1.25, 4000);

	private void ZoomOut_Click(object sender, RoutedEventArgs e) => PageWidth = Math.Max(PageWidth / 1.25, 200);

	private void OpenExternally_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			OpenExternally(_path);
		}
		catch (Exception exception)
		{
			StatusText.Text = "Windows could not open the file: " + PharmaBill.Core.UserMessage.Of(exception);
		}
	}
}
