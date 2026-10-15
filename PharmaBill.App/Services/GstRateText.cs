using System.Globalization;

namespace PharmaBill.App.Services;

/// <summary>Parses a GST rate typed by a user. Blank parses to null ("not entered"), never to 0.</summary>
public static class GstRateText
{
	public static bool TryParse(string? text, out decimal? rate)
	{
		rate = null;
		string trimmed = (text ?? string.Empty).Trim().TrimEnd('%').Trim();
		if (trimmed.Length == 0)
		{
			return true;
		}

		if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value)
			|| decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
		{
			rate = value;
			return true;
		}

		return false;
	}

	public static string Format(decimal? rate, bool isNilRated) =>
		isNilRated ? PharmaBill.Core.Gst.ProductTax.NilRatedLabel : rate is > 0m ? rate.Value.ToString("0.##") + "%" : "?";
}
