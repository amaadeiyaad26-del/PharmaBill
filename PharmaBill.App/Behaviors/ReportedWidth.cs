using System.Windows;

namespace PharmaBill.App.Behaviors;

/// <summary>
/// Reports an element's width to a view-model property as it changes (ActualWidth itself cannot be bound to a source):
/// set <c>IsEnabled="True"</c> and bind <c>Width</c>.
/// </summary>
public static class ReportedWidth
{
	public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
		"IsEnabled", typeof(bool), typeof(ReportedWidth), new PropertyMetadata(false, OnIsEnabledChanged));

	public static readonly DependencyProperty WidthProperty = DependencyProperty.RegisterAttached(
		"Width", typeof(double), typeof(ReportedWidth), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

	public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

	public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

	public static double GetWidth(DependencyObject element) => (double)element.GetValue(WidthProperty);

	public static void SetWidth(DependencyObject element, double value) => element.SetValue(WidthProperty, value);

	private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
	{
		if (element is not FrameworkElement framework)
		{
			return;
		}
		framework.SizeChanged -= OnSizeChanged;
		if (e.NewValue is true)
		{
			framework.SizeChanged += OnSizeChanged;
			if (framework.ActualWidth > 0)
			{
				SetWidth(framework, framework.ActualWidth);
			}
		}
	}

	private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (e.WidthChanged)
		{
			SetWidth((DependencyObject)sender, e.NewSize.Width);
		}
	}
}
