using System;
using System.Globalization;
using System.Text;

namespace PharmaBill.App.Services;

public sealed record Gs1Scan(string Raw, string? Gtin, string? Batch, DateOnly? Expiry)
{
	public static Gs1Scan Parse(string? raw)
	{
		string text = (raw ?? string.Empty).Trim().TrimStart(']').Trim();
		if (text.StartsWith("d2", StringComparison.OrdinalIgnoreCase))
		{
			text = text[2..];
		}

		string? gtin = null;
		string? batch = null;
		DateOnly? expiry = null;
		string body = text;
		if (body.Contains('(') && body.Contains(')'))
		{
			body = ExpandParentheses(body);
		}

		int index = 0;
		while (index < body.Length)
		{
			if (body[index] == '\u001d')
			{
				index++;
				continue;
			}

			if (index + 2 > body.Length)
			{
				break;
			}

			string ai = body.Substring(index, 2);
			index += 2;
			if (ai == "01" && index + 14 <= body.Length)
			{
				gtin = body.Substring(index, 14).TrimStart('0');
				if (gtin.Length == 0)
				{
					gtin = body.Substring(index, 14);
				}

				index += 14;
				continue;
			}

			if (ai == "17" && index + 6 <= body.Length && TryExpiry(body.Substring(index, 6), out DateOnly parsed))
			{
				expiry = parsed;
				index += 6;
				continue;
			}

			if (ai == "10")
			{
				int end = body.IndexOf('\u001d', index);
				if (end < 0)
				{
					end = body.Length;
				}

				batch = body[index..end].Trim();
				index = end;
				continue;
			}

			break;
		}

		if (gtin == null && batch == null && expiry == null)
		{
			return new Gs1Scan(text, null, null, null);
		}

		return new Gs1Scan(text, gtin, string.IsNullOrWhiteSpace(batch) ? null : batch, expiry);
	}

	/// <summary>GTIN and raw code variants used to match a medicine barcode.</summary>
	public IReadOnlyList<string> LookupKeys()
	{
		List<string> keys = new List<string>();
		AddKey(keys, Gtin);
		if (!string.IsNullOrWhiteSpace(Gtin))
		{
			AddKey(keys, Gtin.PadLeft(13, '0'));
			AddKey(keys, Gtin.PadLeft(14, '0'));
		}

		AddKey(keys, Raw);
		return keys;
	}

	private static void AddKey(List<string> keys, string? value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length >= 3 && !keys.Contains(text, StringComparer.OrdinalIgnoreCase))
		{
			keys.Add(text);
		}
	}

	private static string ExpandParentheses(string value)
	{
		StringBuilder builder = new StringBuilder(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			if (value[i] == '(')
			{
				int close = value.IndexOf(')', i + 1);
				if (close > i + 1)
				{
					builder.Append(value.AsSpan(i + 1, close - i - 1));
					i = close;
					continue;
				}
			}

			builder.Append(value[i]);
		}

		return builder.ToString();
	}

	private static bool TryExpiry(string yymmdd, out DateOnly expiry)
	{
		expiry = default;
		if (!int.TryParse(yymmdd, NumberStyles.None, CultureInfo.InvariantCulture, out _))
		{
			return false;
		}

		int year = 2000 + int.Parse(yymmdd[..2], CultureInfo.InvariantCulture);
		int month = int.Parse(yymmdd.Substring(2, 2), CultureInfo.InvariantCulture);
		int day = int.Parse(yymmdd.Substring(4, 2), CultureInfo.InvariantCulture);
		if (month is < 1 or > 12)
		{
			return false;
		}

		int last = DateTime.DaysInMonth(year, month);
		if (day is < 1 or > 31)
		{
			day = last;
		}

		if (day > last)
		{
			day = last;
		}

		expiry = new DateOnly(year, month, day);
		return true;
	}
}
