using System.Globalization;

namespace PharmaBill.Core.Inventory;

public static class ExpiryTextFormatter
{
	/// <summary>Normalises "1026", "10/26", "10-2026", "102026", "10.26" to MM/YYYY. Returns null if not recognisable.</summary>
	public static string? TryFormat(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string value = text.Trim();
		string[] parts = value.Split(new[] { '/', '-', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		string monthText;
		string yearText;
		if (parts.Length == 2)
		{
			monthText = parts[0];
			yearText = parts[1];
		}
		else if (parts.Length == 1 && parts[0].All(char.IsDigit) && parts[0].Length is 4 or 6)
		{
			monthText = parts[0].Substring(0, 2);
			yearText = parts[0].Substring(2);
		}
		else
		{
			return null;
		}

		if (!int.TryParse(monthText, NumberStyles.None, CultureInfo.InvariantCulture, out int month) || month is < 1 or > 12
			|| !int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out int year))
		{
			return null;
		}
		if (yearText.Length == 2)
		{
			year += 2000;
		}
		else if (yearText.Length != 4)
		{
			return null;
		}
		return $"{month:00}/{year:0000}";
	}
}
