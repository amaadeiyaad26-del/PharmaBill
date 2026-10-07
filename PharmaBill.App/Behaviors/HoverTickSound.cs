using System.Windows;
using System.Windows.Input;
using PharmaBill.App.Services;

namespace PharmaBill.App.Behaviors;

public static class HoverTickSound
{
	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(HoverTickSound), new PropertyMetadata(false, OnEnabledChanged));

	public static bool GetEnabled(DependencyObject element)
	{
		return (bool)element.GetValue(EnabledProperty);
	}

	public static void SetEnabled(DependencyObject element, bool value)
	{
		element.SetValue(EnabledProperty, value);
	}

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is UIElement uIElement)
		{
			uIElement.MouseEnter -= OnMouseEnter;
			if ((bool)e.NewValue)
			{
				uIElement.MouseEnter += OnMouseEnter;
			}
		}
	}

	private static void OnMouseEnter(object sender, MouseEventArgs e)
	{
		SoundHelper.PlayHoverTick();
	}
}
