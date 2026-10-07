using System;
using System.Globalization;
using System.Windows.Data;

namespace PharmaBill.App.Controls;

public sealed class StockButtonTextConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (!(value is bool) || !(bool)value)
		{
			return "Add to stock";
		}
		return "Add stock (new batch)";
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
