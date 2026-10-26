using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PharmaBill.App.Behaviors;

/// <summary>
/// Closes a search flyout when the user clicks anywhere outside it and its search box, presses Esc, or the window moves
/// or loses focus. Unlike StaysOpen="False", the outside click still reaches what was clicked, so a cashier who types and
/// then clicks "+ Add" or the bill never has to click twice.
/// </summary>
public static class FlyoutDismiss
{
	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(FlyoutDismiss), new PropertyMetadata(false, OnEnabledChanged));

	private static readonly DependencyProperty HookProperty = DependencyProperty.RegisterAttached("Hook", typeof(WindowHook), typeof(FlyoutDismiss));

	public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

	public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not Popup popup)
		{
			return;
		}
		popup.Opened -= OnOpened;
		popup.Closed -= OnClosed;
		if ((bool)e.NewValue)
		{
			popup.Opened += OnOpened;
			popup.Closed += OnClosed;
		}
	}

	private static void OnOpened(object? sender, EventArgs e)
	{
		if (sender is not Popup popup || Window.GetWindow(popup.PlacementTarget ?? popup) is not Window window)
		{
			return;
		}
		(popup.GetValue(HookProperty) as WindowHook)?.Detach();
		popup.SetValue(HookProperty, new WindowHook(popup, window));
	}

	private static void OnClosed(object? sender, EventArgs e)
	{
		if (sender is Popup popup && popup.GetValue(HookProperty) is WindowHook hook)
		{
			hook.Detach();
			popup.ClearValue(HookProperty);
		}
	}

	/// <summary>Watches the window while the flyout is open.</summary>
	private sealed class WindowHook
	{
		private readonly Popup _popup;
		private readonly Window _window;

		public WindowHook(Popup popup, Window window)
		{
			_popup = popup;
			_window = window;
			window.PreviewMouseDown += OnMouseDown;
			window.PreviewKeyDown += OnKeyDown;
			window.Deactivated += OnWindowChanged;
			window.LocationChanged += OnWindowChanged;
			window.SizeChanged += OnWindowChanged;
		}

		public void Detach()
		{
			_window.PreviewMouseDown -= OnMouseDown;
			_window.PreviewKeyDown -= OnKeyDown;
			_window.Deactivated -= OnWindowChanged;
			_window.LocationChanged -= OnWindowChanged;
			_window.SizeChanged -= OnWindowChanged;
		}

		private void OnMouseDown(object sender, MouseButtonEventArgs e)
		{
			// Clicks inside the flyout, or back in its search box, keep it open. Nothing is handled: the click goes on.
			if (e.OriginalSource is DependencyObject source && (IsWithin(source, _popup.Child) || IsWithin(source, _popup.PlacementTarget)))
			{
				return;
			}
			Close();
		}

		private void OnKeyDown(object sender, KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
				Close();
				e.Handled = true;
			}
		}

		private void OnWindowChanged(object? sender, EventArgs e) => Close();

		private void Close() => _popup.SetCurrentValue(Popup.IsOpenProperty, false);
	}

	public static bool IsWithin(DependencyObject? element, DependencyObject? container)
	{
		if (container == null)
		{
			return false;
		}
		for (DependencyObject? current = element; current != null; current = Parent(current))
		{
			if (ReferenceEquals(current, container))
			{
				return true;
			}
		}
		return false;
	}

	private static DependencyObject? Parent(DependencyObject element) =>
		element is Visual or System.Windows.Media.Media3D.Visual3D
			? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element)
			: LogicalTreeHelper.GetParent(element);
}
