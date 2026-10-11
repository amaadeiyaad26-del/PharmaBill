using System;
using System.Globalization;
using System.Windows.Data;

namespace PharmaBill.App.Controls;

/// <summary>
/// True when the active stock filter (values[0]) is the filter a stock tab stands for (values[1]),
/// so the tab can draw itself as selected.
/// </summary>
public sealed class StockTabMatchConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		return values.Length == 2
			&& values[0] is string active
			&& values[1] is string tab
			&& string.Equals(active, tab, StringComparison.OrdinalIgnoreCase);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
