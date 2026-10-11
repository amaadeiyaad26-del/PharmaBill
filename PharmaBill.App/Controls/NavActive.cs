using System.Windows;

namespace PharmaBill.App.Controls;

/// <summary>
/// Marks a sidebar button as the screen that is open. The button style turns it green while this is true.
/// A typed bool keeps the style trigger's Value="True" comparison exact.
/// </summary>
public static class NavActive
{
	public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
		"IsActive",
		typeof(bool),
		typeof(NavActive),
		new PropertyMetadata(false));

	public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

	public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
}
