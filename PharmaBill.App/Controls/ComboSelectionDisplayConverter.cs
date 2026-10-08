using System;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace PharmaBill.App.Controls;

/// <summary>
/// Resolves ComboBox SelectionBoxItem to a readable label via DisplayMemberPath / Name.
/// Fixes FloatingInnerComboBox templates that otherwise fall back to type.ToString().
/// </summary>
public sealed class ComboSelectionDisplayConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		if (values == null || values.Length == 0 || values[0] == null || values[0] == System.Windows.DependencyProperty.UnsetValue)
		{
			return string.Empty;
		}

		object item = values[0];
		if (item is string text)
		{
			return text;
		}

		string? path = values.Length > 1 ? values[1] as string : null;
		if (string.IsNullOrWhiteSpace(path))
		{
			path = "Name";
		}

		PropertyInfo? property = item.GetType().GetProperty(path, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
		if (property != null)
		{
			object? value = property.GetValue(item);
			if (value != null)
			{
				return value.ToString() ?? string.Empty;
			}
		}

		foreach (string fallback in new[] { "Name", "DisplayName", "Title", "Label", "Display" })
		{
			if (string.Equals(fallback, path, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			PropertyInfo? alt = item.GetType().GetProperty(fallback, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
			if (alt?.GetValue(item) is { } altValue)
			{
				return altValue.ToString() ?? string.Empty;
			}
		}

		string raw = item.ToString() ?? string.Empty;
		return raw.StartsWith("PharmaBill.", StringComparison.Ordinal) ? string.Empty : raw;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}