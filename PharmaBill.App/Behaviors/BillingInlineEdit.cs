using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PharmaBill.App.Behaviors;

/// <summary>
/// Always-visible billing line editors: select-all on focus, Enter advances fields,
/// and optional focus-request targeting the Qty box of the selected row.
/// </summary>
public static class BillingInlineEdit
{
	public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached(
		"Field",
		typeof(string),
		typeof(BillingInlineEdit),
		new PropertyMetadata(null, OnFieldChanged));

	public static readonly DependencyProperty SearchElementNameProperty = DependencyProperty.RegisterAttached(
		"SearchElementName",
		typeof(string),
		typeof(BillingInlineEdit),
		new PropertyMetadata(null));

	public static readonly DependencyProperty FocusQtyRequestProperty = DependencyProperty.RegisterAttached(
		"FocusQtyRequest",
		typeof(string),
		typeof(BillingInlineEdit),
		new PropertyMetadata(null, OnFocusQtyRequestChanged));

	public static readonly DependencyProperty FocusTargetFieldProperty = DependencyProperty.RegisterAttached(
		"FocusTargetField",
		typeof(string),
		typeof(BillingInlineEdit),
		new PropertyMetadata("Qty"));

	public static string GetField(DependencyObject element) => (string)element.GetValue(FieldProperty);

	public static void SetField(DependencyObject element, string value) => element.SetValue(FieldProperty, value);

	public static string GetSearchElementName(DependencyObject element) => (string)element.GetValue(SearchElementNameProperty);

	public static void SetSearchElementName(DependencyObject element, string value) => element.SetValue(SearchElementNameProperty, value);

	public static string GetFocusQtyRequest(DependencyObject element) => (string)element.GetValue(FocusQtyRequestProperty);

	public static void SetFocusQtyRequest(DependencyObject element, string value) => element.SetValue(FocusQtyRequestProperty, value);

	public static string GetFocusTargetField(DependencyObject element) => (string)element.GetValue(FocusTargetFieldProperty);

	public static void SetFocusTargetField(DependencyObject element, string value) => element.SetValue(FocusTargetFieldProperty, value);

	private static void OnFieldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not Control control)
		{
			return;
		}

		control.GotKeyboardFocus -= OnGotKeyboardFocus;
		control.PreviewKeyDown -= OnPreviewKeyDown;
		if (e.NewValue is string { Length: > 0 })
		{
			control.GotKeyboardFocus += OnGotKeyboardFocus;
			control.PreviewKeyDown += OnPreviewKeyDown;
		}
	}

	private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (sender is TextBox textBox && !textBox.IsReadOnly)
		{
			textBox.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
			{
				if (textBox.IsKeyboardFocusWithin)
				{
					textBox.SelectAll();
				}
			});
		}
	}

	private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if ((e.Key != Key.Enter && e.Key != Key.Return) || sender is not FrameworkElement element)
		{
			return;
		}

		bool reverse = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
		e.Handled = true;
		string field = GetField(element) ?? string.Empty;
		DataGrid? grid = FindAncestor<DataGrid>(element);
		DataGridRow? row = FindAncestor<DataGridRow>(element);
		if (grid == null || row == null)
		{
			return;
		}

		if (reverse)
		{
			string? previous = field switch
			{
				"Discount" => "Rate",
				"Rate" => HasField(row, "Free") ? "Free" : "Qty",
				"Free" => "Qty",
				"Qty" => HasField(row, "Unit") ? "Unit" : null,
				"Unit" => null,
				_ => null
			};
			if (previous != null)
			{
				FrameworkElement? prevTarget = FindFieldInRow(row, previous);
				if (prevTarget != null)
				{
					FocusAndSelect(prevTarget);
					return;
				}
			}

			string? searchBack = GetSearchElementName(grid);
			if (!string.IsNullOrWhiteSpace(searchBack))
			{
				FocusAndSelect(FindNamedDescendant(Window.GetWindow(grid) ?? (DependencyObject)grid, searchBack));
			}
			return;
		}

		if (string.Equals(field, "Qty", StringComparison.OrdinalIgnoreCase))
		{
			string? searchFromQty = GetSearchElementName(grid);
			if (!string.IsNullOrWhiteSpace(searchFromQty))
			{
				FocusAndSelect(FindNamedDescendant(Window.GetWindow(grid) ?? (DependencyObject)grid, searchFromQty));
			}

			return;
		}

		string? next = field switch
		{
			"Unit" => "Qty",
			"Free" => "Rate",
			"Rate" => HasField(row, "Discount") ? "Discount" : null,
			"Discount" => null,
			_ => null
		};

		if (next != null)
		{
			FrameworkElement? target = FindFieldInRow(row, next);
			if (target != null)
			{
				FocusAndSelect(target);
				return;
			}
		}

		string? searchName = GetSearchElementName(grid);
		if (!string.IsNullOrWhiteSpace(searchName))
		{
			FrameworkElement? search = FindNamedDescendant(Window.GetWindow(grid) ?? (DependencyObject)grid, searchName);
			FocusAndSelect(search);
		}
	}

	private static void OnFocusQtyRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is not DataGrid grid || e.NewValue is not string token || string.IsNullOrWhiteSpace(token))
		{
			return;
		}

		string field = GetFocusTargetField(grid);
		grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusSelectedField(grid, string.IsNullOrWhiteSpace(field) ? "Qty" : field, 0));
	}

	private static void FocusSelectedField(DataGrid grid, string field, int attempt)
	{
		grid.UpdateLayout();
		object? selected = grid.SelectedItem;
		if (selected == null)
		{
			return;
		}

		grid.ScrollIntoView(selected);
		grid.UpdateLayout();
		if (grid.ItemContainerGenerator.ContainerFromItem(selected) is DataGridRow row)
		{
			FrameworkElement? qty = FindFieldInRow(row, field);
			if (qty != null)
			{
				FocusAndSelect(qty);
				return;
			}
		}

		if (attempt < 5)
		{
			grid.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FocusSelectedField(grid, field, attempt + 1));
		}
	}

	private static bool HasField(DataGridRow row, string field) => FindFieldInRow(row, field) != null;

	private static FrameworkElement? FindFieldInRow(DataGridRow row, string field)
	{
		return FindFieldRecursive(row, field);
	}

	private static FrameworkElement? FindFieldRecursive(DependencyObject parent, string field)
	{
		int count = VisualTreeHelper.GetChildrenCount(parent);
		for (int i = 0; i < count; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is FrameworkElement fe && string.Equals(GetField(fe), field, System.StringComparison.OrdinalIgnoreCase))
			{
				return fe;
			}

			FrameworkElement? nested = FindFieldRecursive(child, field);
			if (nested != null)
			{
				return nested;
			}
		}

		return null;
	}

	private static FrameworkElement? FindNamedDescendant(DependencyObject parent, string name)
	{
		if (parent is FrameworkElement { Name: var n } self && string.Equals(n, name, System.StringComparison.Ordinal))
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

	private static void FocusAndSelect(FrameworkElement? element)
	{
		if (element == null)
		{
			return;
		}

		element.Focus();
		if (element is TextBox textBox)
		{
			textBox.SelectAll();
		}
		else if (element is ComboBox combo)
		{
			combo.Focus();
		}
	}

	private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
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
}