using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace PharmaBill.App.Behaviors;

/// <summary>
/// Keyboard-first navigation: Enter advances fields, Shift+Enter/Tab goes back,
/// SelectAll on focus, and Esc closes suggestion popups within a scope.
/// </summary>
public static class KeyNavigationBehavior
{
	public static readonly DependencyProperty EnterAsTabProperty = DependencyProperty.RegisterAttached(
		"EnterAsTab",
		typeof(bool),
		typeof(KeyNavigationBehavior),
		new PropertyMetadata(false, OnEnterAsTabChanged));

	public static readonly DependencyProperty SelectAllOnFocusProperty = DependencyProperty.RegisterAttached(
		"SelectAllOnFocus",
		typeof(bool),
		typeof(KeyNavigationBehavior),
		new PropertyMetadata(false, OnSelectAllOnFocusChanged));

	public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
		"Enabled",
		typeof(bool),
		typeof(KeyNavigationBehavior),
		new PropertyMetadata(false, OnEnabledChanged));

	public static readonly DependencyProperty SubmitCommandProperty = DependencyProperty.RegisterAttached(
		"SubmitCommand",
		typeof(ICommand),
		typeof(KeyNavigationBehavior),
		new PropertyMetadata(null));

	public static readonly DependencyProperty EnterSubmitsProperty = DependencyProperty.RegisterAttached(
		"EnterSubmits",
		typeof(bool),
		typeof(KeyNavigationBehavior),
		new PropertyMetadata(false));

	public static readonly DependencyProperty CycleFocusElementNameProperty = DependencyProperty.RegisterAttached(
		"CycleFocusElementName",
		typeof(string),
		typeof(KeyNavigationBehavior),
		new PropertyMetadata(null));

	public static bool GetEnterAsTab(DependencyObject element) => (bool)element.GetValue(EnterAsTabProperty);

	public static void SetEnterAsTab(DependencyObject element, bool value) => element.SetValue(EnterAsTabProperty, value);

	public static bool GetSelectAllOnFocus(DependencyObject element) => (bool)element.GetValue(SelectAllOnFocusProperty);

	public static void SetSelectAllOnFocus(DependencyObject element, bool value) => element.SetValue(SelectAllOnFocusProperty, value);

	public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);

	public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

	public static ICommand? GetSubmitCommand(DependencyObject element) => (ICommand?)element.GetValue(SubmitCommandProperty);

	public static void SetSubmitCommand(DependencyObject element, ICommand? value) => element.SetValue(SubmitCommandProperty, value);

	public static bool GetEnterSubmits(DependencyObject element) => (bool)element.GetValue(EnterSubmitsProperty);

	public static void SetEnterSubmits(DependencyObject element, bool value) => element.SetValue(EnterSubmitsProperty, value);

	public static string? GetCycleFocusElementName(DependencyObject element) => (string?)element.GetValue(CycleFocusElementNameProperty);

	public static void SetCycleFocusElementName(DependencyObject element, string? value) => element.SetValue(CycleFocusElementNameProperty, value);

	private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		bool enabled = (bool)e.NewValue;
		SetEnterAsTab(d, enabled);
		SetSelectAllOnFocus(d, enabled);
	}

	private static void OnEnterAsTabChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not UIElement element)
		{
			return;
		}

		element.PreviewKeyDown -= OnPreviewKeyDown;
		if ((bool)e.NewValue)
		{
			element.PreviewKeyDown += OnPreviewKeyDown;
		}
	}

	private static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not UIElement element)
		{
			return;
		}

		element.RemoveHandler(UIElement.GotKeyboardFocusEvent, (KeyboardFocusChangedEventHandler)OnGotKeyboardFocus);
		if ((bool)e.NewValue)
		{
			element.AddHandler(UIElement.GotKeyboardFocusEvent, (KeyboardFocusChangedEventHandler)OnGotKeyboardFocus, true);
		}
	}

	private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (e.NewFocus is not DependencyObject focused)
		{
			return;
		}

		if (!KeyboardFocusManager.IsNavigableInput(focused) && focused is not TextBox)
		{
			return;
		}

		if (focused is UIElement ui)
		{
			ui.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
			{
				if (Keyboard.FocusedElement == focused || (focused is FrameworkElement fe && fe.IsKeyboardFocusWithin))
				{
					KeyboardFocusManager.SelectAll(focused);
				}
			});
		}
	}

	private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (sender is not DependencyObject scope)
		{
			return;
		}

		Key key = e.Key == Key.System ? e.SystemKey : e.Key;

		if (key == Key.Escape)
		{
			if (KeyboardFocusManager.TryCloseOpenPopup(e.OriginalSource as DependencyObject))
			{
				e.Handled = true;
			}
			return;
		}

		bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
		bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

		if (key == Key.Tab && shift)
		{
			return;
		}

		if (key != Key.Enter && key != Key.Return)
		{
			return;
		}

		DependencyObject? source = e.OriginalSource as DependencyObject;
		if (source == null)
		{
			return;
		}

		if (KeyboardFocusManager.IsMultilineTextBox(source) || KeyboardFocusManager.IsMultilineTextBox(KeyboardFocusManager.FindAncestor<TextBox>(source)))
		{
			return;
		}

		if (KeyboardFocusManager.IsSuggestionPopupActive(source) && !shift)
		{
			return;
		}

		if (!shift && !ctrl && EnterSubmits(source))
		{
			ICommand? submitNow = GetSubmitCommand(scope);
			if (submitNow != null && submitNow.CanExecute(null))
			{
				submitNow.Execute(null);
				e.Handled = true;
			}

			return;
		}

		if (ctrl && !shift)
		{
			ICommand? submit = GetSubmitCommand(scope);
			if (submit != null && submit.CanExecute(null))
			{
				submit.Execute(null);
				string? cycleName = GetCycleFocusElementName(scope);
				if (!string.IsNullOrWhiteSpace(cycleName))
				{
					FrameworkElement? root = KeyboardFocusManager.FindAncestor<Window>(scope) ?? scope as FrameworkElement;
					if (root != null)
					{
						KeyboardFocusManager.FocusAndSelect(KeyboardFocusManager.FindNamedDescendant(root, cycleName));
					}
				}
				e.Handled = true;
			}
			return;
		}

		e.Handled = true;
		if (shift)
		{
			KeyboardFocusManager.MovePrevious();
		}
		else
		{
			KeyboardFocusManager.MoveNext();
		}
	}

	private static bool EnterSubmits(DependencyObject source)
	{
		DependencyObject? current = source;
		while (current != null)
		{
			if (current.GetValue(EnterSubmitsProperty) is true)
			{
				return true;
			}

			current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
				? System.Windows.Media.VisualTreeHelper.GetParent(current)
				: System.Windows.LogicalTreeHelper.GetParent(current);
		}

		return false;
	}
}