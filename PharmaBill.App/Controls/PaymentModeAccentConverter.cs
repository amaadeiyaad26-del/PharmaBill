using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PharmaBill.App.Controls;

/// <summary>
/// Gives each payment mode its own colour in the payment dropdown.
/// ConverterParameter "tint" returns a faint wash of the same colour for hover.
/// </summary>
public sealed class PaymentModeAccentConverter : IValueConverter
{
	private static readonly Dictionary<string, Color> Accents = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
	{
		["Cash"] = Color.FromRgb(0x16, 0xA3, 0x4A),
		["UPI / QR"] = Color.FromRgb(0x0E, 0x9F, 0xD8),
		["Card / POS"] = Color.FromRgb(0x25, 0x63, 0xEB),
		["Cheque / Bank Transfer"] = Color.FromRgb(0x0D, 0x94, 0x88),
		["Credit / Ledger"] = Color.FromRgb(0xEA, 0x58, 0x0C),
		["Loyalty points"] = Color.FromRgb(0xCA, 0x8A, 0x04),
		["Split Payment"] = Color.FromRgb(0xDB, 0x27, 0x77),
	};

	private static readonly Color Fallback = Color.FromRgb(0x63, 0x66, 0xF1);

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		Color color = value is string mode && Accents.TryGetValue(mode, out Color found) ? found : Fallback;
		if (string.Equals(parameter as string, "tint", StringComparison.OrdinalIgnoreCase))
		{
			color = Color.FromArgb(0x26, color.R, color.G, color.B);
		}

		SolidColorBrush brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
