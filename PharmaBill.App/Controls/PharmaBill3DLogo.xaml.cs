using System.Windows;
using System.Windows.Controls;

namespace PharmaBill.App.Controls;

/// <summary>
/// Pure WPF vector "PB" 3D brand emblem — no raster cutouts, no dark plate artifacts.
/// </summary>
public partial class PharmaBill3DLogo : UserControl
{
	public static readonly DependencyProperty ShowAmbientShadowProperty = DependencyProperty.Register(
		nameof(ShowAmbientShadow),
		typeof(bool),
		typeof(PharmaBill3DLogo),
		new PropertyMetadata(true));

	public bool ShowAmbientShadow
	{
		get => (bool)GetValue(ShowAmbientShadowProperty);
		set => SetValue(ShowAmbientShadowProperty, value);
	}

	public PharmaBill3DLogo()
	{
		InitializeComponent();
	}
}
