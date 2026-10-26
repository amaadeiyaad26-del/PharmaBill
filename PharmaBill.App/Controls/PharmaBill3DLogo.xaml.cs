using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace PharmaBill.App.Controls;

/// <summary>Which crop of the brand artwork to show.</summary>
public enum BrandLogoVariant
{
	/// <summary>Capsule and caduceus emblem only. Use beside a "Pharma Remit" caption or at small sizes.</summary>
	Mark,

	/// <summary>Emblem with the "PharmaRemit" wordmark.</summary>
	Full
}

/// <summary>The Pharma Remit brand logo (raster artwork in Resources).</summary>
public partial class PharmaBill3DLogo : UserControl
{
	private const string MarkUri = "pack://application:,,,/PharmaBill.App;component/Resources/brand_mark.png";

	private const string FullUri = "pack://application:,,,/PharmaBill.App;component/Resources/logo_final_3d.png";

	public static readonly DependencyProperty ShowAmbientShadowProperty = DependencyProperty.Register(
		nameof(ShowAmbientShadow),
		typeof(bool),
		typeof(PharmaBill3DLogo),
		new PropertyMetadata(true));

	public static readonly DependencyProperty VariantProperty = DependencyProperty.Register(
		nameof(Variant),
		typeof(BrandLogoVariant),
		typeof(PharmaBill3DLogo),
		new PropertyMetadata(BrandLogoVariant.Mark, (d, _) => ((PharmaBill3DLogo)d).UpdateSource()));

	public bool ShowAmbientShadow
	{
		get => (bool)GetValue(ShowAmbientShadowProperty);
		set => SetValue(ShowAmbientShadowProperty, value);
	}

	public BrandLogoVariant Variant
	{
		get => (BrandLogoVariant)GetValue(VariantProperty);
		set => SetValue(VariantProperty, value);
	}

	public PharmaBill3DLogo()
	{
		InitializeComponent();
		UpdateSource();
	}

	private void UpdateSource()
	{
		if (LogoImage == null)
		{
			return;
		}

		BitmapImage image = new BitmapImage();
		image.BeginInit();
		image.UriSource = new Uri(Variant == BrandLogoVariant.Full ? FullUri : MarkUri, UriKind.Absolute);
		image.CacheOption = BitmapCacheOption.OnLoad;
		image.EndInit();
		image.Freeze();
		LogoImage.Source = image;
	}
}
