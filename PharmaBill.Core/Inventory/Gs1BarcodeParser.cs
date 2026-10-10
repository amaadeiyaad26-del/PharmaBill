using System.Globalization;

namespace PharmaBill.Core.Inventory;

public sealed record Gs1ScanResult(string Barcode, string? Gtin, string? Batch, string? ExpiryText, bool IsGs1);

/// <summary>Parses GS1 DataMatrix / GS1-128 payloads: (01) GTIN, (10) batch, (17) expiry YYMMDD.</summary>
public static class Gs1BarcodeParser
{
	private const char GroupSeparator = '\u001d';

	public static Gs1ScanResult Parse(string? raw)
	{
		string value = (raw ?? string.Empty).Trim();
		value = StripSymbologyPrefix(value);
		if (value.Length == 0)
		{
			return new Gs1ScanResult(string.Empty, null, null, null, false);
		}

		bool bracketed = value.StartsWith("(01)", StringComparison.Ordinal);
		if (bracketed)
		{
			value = value.Replace("(", string.Empty).Replace(")", string.Empty);
		}

		bool looksGs1 = bracketed || value.Contains(GroupSeparator) || (value.Length > 16 && value.StartsWith("01", StringComparison.Ordinal) && value.Skip(2).Take(14).All(char.IsDigit));
		if (!looksGs1)
		{
			return new Gs1ScanResult(value, null, null, null, false);
		}

		string? gtin = null;
		string? batch = null;
		string? expiry = null;
		int index = 0;
		while (index + 2 <= value.Length)
		{
			string ai = value.Substring(index, 2);
			int fixedLength = ai switch { "01" => 14, "17" => 6, "11" => 6, "15" => 6, _ => -1 };
			if (fixedLength < 0 && ai != "10" && ai != "21")
			{
				break;
			}
			index += 2;
			int length = fixedLength;
			if (length < 0)
			{
				int separator = value.IndexOf(GroupSeparator, index);
				length = separator < 0 ? value.Length - index : separator - index;
			}
			if (length <= 0 || index + length > value.Length)
			{
				break;
			}
			string data = value.Substring(index, length);
			index += length;
			while (index < value.Length && value[index] == GroupSeparator)
			{
				index++;
			}
			switch (ai)
			{
			case "01": gtin = data; break;
			case "10": batch = data; break;
			case "17": expiry = ExpiryFromYyMmDd(data); break;
			}
		}

		if (gtin == null && batch == null && expiry == null)
		{
			return new Gs1ScanResult(value, null, null, null, false);
		}
		return new Gs1ScanResult(gtin ?? value, gtin, batch, expiry, true);
	}

	/// <summary>GS1 day "00" means the end of the month.</summary>
	public static string? ExpiryFromYyMmDd(string data)
	{
		if (data.Length != 6 || !data.All(char.IsDigit))
		{
			return null;
		}
		int year = 2000 + int.Parse(data.AsSpan(0, 2), CultureInfo.InvariantCulture);
		int month = int.Parse(data.AsSpan(2, 2), CultureInfo.InvariantCulture);
		return month is >= 1 and <= 12 ? $"{month:00}/{year:0000}" : null;
	}

	private static string StripSymbologyPrefix(string value)
	{
		return value.Length > 3 && value[0] == ']' && char.IsLetter(value[1]) && char.IsDigit(value[2]) ? value.Substring(3) : value;
	}
}
