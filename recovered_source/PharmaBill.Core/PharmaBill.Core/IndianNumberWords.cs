using System;
using System.Globalization;
using System.Linq;

namespace PharmaBill.Core;

public static class IndianNumberWords
{
	private static readonly string[] Ones = new string[20]
	{
		"zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
		"ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen"
	};

	private static readonly string[] Tens = new string[10] { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

	public static string Convert(decimal amount)
	{
		if (amount < 0m)
		{
			throw new ArgumentOutOfRangeException("amount", "Amount cannot be negative.");
		}
		decimal num = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
		long num2 = decimal.ToInt64(decimal.Floor(num));
		int num3 = (int)((num - (decimal)num2) * 100m);
		if (num2 > 999999999999L)
		{
			throw new ArgumentOutOfRangeException("amount", "Amount exceeds the supported Indian numbering range.");
		}
		string text = NumberToWords(num2);
		string text2 = ((num2 == 1) ? "rupee" : "rupees");
		if (num3 != 0)
		{
			return $"{text} {text2} and {NumberToWords(num3)} paise only";
		}
		return text + " " + text2 + " only";
	}

	private static string NumberToWords(long value)
	{
		if (value < 20)
		{
			return Ones[value];
		}
		if (value < 100)
		{
			return string.Join(' ', new string[2]
			{
				Tens[value / 10],
				NumberToWords(value % 10)
			}.Where((string part) => part != "zero"));
		}
		(long, string)[] array = new (long, string)[6]
		{
			(100000000000L, "kharab"),
			(1000000000L, "arab"),
			(10000000L, "crore"),
			(100000L, "lakh"),
			(1000L, "thousand"),
			(100L, "hundred")
		};
		for (int num = 0; num < array.Length; num++)
		{
			var (num2, text) = array[num];
			if (value >= num2)
			{
				long value2 = value / num2;
				long num3 = value % num2;
				if (num3 != 0L)
				{
					return $"{NumberToWords(value2)} {text} {NumberToWords(num3)}";
				}
				return NumberToWords(value2) + " " + text;
			}
		}
		return value.ToString(CultureInfo.InvariantCulture);
	}
}
