using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PharmaBill.App.Behaviors;

/// <summary>
/// Shared focus helpers for keyboard-first billing / purchase / stock entry.
/// </summary>
public static class KeyboardFocusManager
{
	public static bool IsMultilineTextBox(DependencyObject? element)
	{
		return element is TextBox { AcceptsReturn: true };
	}

	public static bool IsNavigableInput(DependencyObject? element)
	{
		return element switch
		{
			TextBox tb => !tb.AcceptsReturn && !tb.IsReadOnly,
			PasswordBox => true,
			ComboBox => true,
			DatePicker => true,
			_ => false
		};
	}

	public static bool TryMoveFocus(FocusNavigationDirection direction)
	{
		if (Keyboard.FocusedElement is not UIElement current)
		{
			return false;
		}

		return current.MoveFocus(new TraversalRequest(direction));
	}

	public static bool MoveNext() => TryMoveFocus(FocusNavigationDirection.Next);

	public static bool MovePrevious() => TryMoveFocus(FocusNavigationDirection.Previous);

	public static void SelectAll(DependencyObject? element)
	{
		switch (element)
		{
			case TextBox textBox when !textBox.IsReadOnly:
				textBox.SelectAll();
				break;
			case PasswordBox passwordBox:
				passwordBox.SelectAll();
				break;
			case ComboBox { IsEditable: true } combo:
				if (combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox editable)
				{
					editable.SelectAll();
				}
				break;
		}
	}

	public static void FocusAndSelect(FrameworkElement? element)
	{
		if (element == null)
		{
			return;
		}

		element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
		{
			if (!element.IsVisible || !element.IsEnabled)
			{
				return;
			}

			element.Focus();
			Keyboard.Focus(element);
			SelectAll(element);

			if (element is ComboBox { IsEditable: true } combo)
			{
				combo.Focus();
				if (combo.Template?.FindName("PART_EditableTextBox", combo) is TextBox editable)
				{
					editable.Focus();
					editable.SelectAll();
				}
			}
		});
	}

	public static FrameworkElement? FindNamedDescendant(DependencyObject parent, string name)
	{
		if (parent is FrameworkElement { Name: var n } self && string.Equals(n, name, StringComparison.Ordinal))
		{
			return self;
		}

		int count = VisualTreeHelper.GetChildrenCount(parent);
		for (int i = 0; i < count; i++)
		{
			FrameworkElement? found = FindNamedDescendant(VisualTreeHelper.GetChild(parent, i), name);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	public static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
	{
		while (current != null)
		{
			if (current is T match)
			{
				return match;
			}

			current = current is Visual or System.Windows.Media.Media3D.Visual3D
				? VisualTreeHelper.GetParent(current)
				: LogicalTreeHelper.GetParent(current);
		}

		return null;
	}

	/// <summary>
	/// True when a ComboBox dropdown / suggestion popup should consume Enter / arrows itself.
	/// </summary>
	public static bool IsSuggestionPopupActive(DependencyObject? source)
	{
		if (source is ComboBox { IsDropDownOpen: true })
		{
			return true;
		}

		ComboBox? combo = FindAncestor<ComboBox>(source);
		if (combo is { IsDropDownOpen: true })
		{
			return true;
		}

		Popup? popup = FindAncestor<Popup>(source);
		return popup is { IsOpen: true };
	}

	public static bool TryCloseOpenPopup(DependencyObject? source)
	{
		if (source is ComboBox { IsDropDownOpen: true } combo)
		{
			combo.IsDropDownOpen = false;
			return true;
		}

		ComboBox? ancestorCombo = FindAncestor<ComboBox>(source);
		if (ancestorCombo is { IsDropDownOpen: true })
		{
			ancestorCombo.IsDropDownOpen = false;
			return true;
		}

		Popup? popup = FindAncestor<Popup>(source);
		if (popup is { IsOpen: true })
		{
			popup.IsOpen = false;
			return true;
		}

		return false;
	}
}