using System.Windows;

namespace PharmaBill.App.Controls;

/// <summary>
/// A page shown as a tab of a hub (Stock Management, Accounts &amp; Parties, Settings) sits under the hub's own title.
/// The hub marks its content <see cref="IsEmbeddedProperty"/> (inherited by everything inside); an element marked
/// <see cref="CollapseWhenEmbeddedProperty"/>, the page's own big title, then hides. Shown on its own, nothing changes.
/// </summary>
public static class HubHost
{
	public static readonly DependencyProperty IsEmbeddedProperty = DependencyProperty.RegisterAttached("IsEmbedded", typeof(bool), typeof(HubHost),
		new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, OnChanged));

	public static readonly DependencyProperty CollapseWhenEmbeddedProperty = DependencyProperty.RegisterAttached("CollapseWhenEmbedded", typeof(bool), typeof(HubHost),
		new PropertyMetadata(false, OnChanged));

	public static bool GetIsEmbedded(DependencyObject element) => (bool)element.GetValue(IsEmbeddedProperty);

	public static void SetIsEmbedded(DependencyObject element, bool value) => element.SetValue(IsEmbeddedProperty, value);

	public static bool GetCollapseWhenEmbedded(DependencyObject element) => (bool)element.GetValue(CollapseWhenEmbeddedProperty);

	public static void SetCollapseWhenEmbedded(DependencyObject element, bool value) => element.SetValue(CollapseWhenEmbeddedProperty, value);

	private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is UIElement element && GetCollapseWhenEmbedded(element))
		{
			element.Visibility = GetIsEmbedded(element) ? Visibility.Collapsed : Visibility.Visible;
		}
	}
}
