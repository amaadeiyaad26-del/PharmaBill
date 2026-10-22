using System;
using System.Globalization;
using System.Windows.Data;
using PharmaBill.Core;

namespace PharmaBill.App.Controls;

/// <summary>Shows a stored UTC time as the shop's (IST) time. ConverterParameter is the format, default "dd-MMM-yyyy HH:mm".</summary>
public sealed class BusinessLocalTimeConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
		value is DateTime utc ? utc.ToBusinessLocal().ToString(parameter as string ?? "dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture) : string.Empty;

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
