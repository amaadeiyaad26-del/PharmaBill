using System;
using System.Globalization;
using System.Windows.Data;

namespace PharmaBill.App.Controls;

public sealed class SectionActiveConverter : IMultiValueConverter
{
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		return values.Length >= 2 && values[0] is string a && values[1] is string b && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
