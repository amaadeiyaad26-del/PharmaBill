using System;
using System.Globalization;
using System.Windows.Data;

namespace PharmaBill.App.Controls;

public sealed class StringEqualsConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		return value is string a && parameter is string b && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
