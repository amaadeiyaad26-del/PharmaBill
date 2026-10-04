using System.Globalization;
using System.Windows.Data;

namespace PharmaBill.App.Controls;

public sealed class StockButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? "Add stock (new batch)" : "Add to stock";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}