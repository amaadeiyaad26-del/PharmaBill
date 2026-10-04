using System.Globalization;

namespace PharmaBill.Core;

public static class IndianNumberWords
{
    private static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
        "eighteen", "nineteen"
    ];

    private static readonly string[] Tens =
    [
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    ];

    public static string Convert(decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        var whole = decimal.ToInt64(decimal.Floor(rounded));
        var paise = (int)((rounded - whole) * 100m);
        if (whole > 999_999_999_999L)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount exceeds the supported Indian numbering range.");
        }

        var words = NumberToWords(whole);
        var currency = whole == 1 ? "rupee" : "rupees";
        return paise == 0
            ? $"{words} {currency} only"
            : $"{words} {currency} and {NumberToWords(paise)} paise only";
    }

    private static string NumberToWords(long value)
    {
        if (value < 20)
        {
            return Ones[value];
        }

        if (value < 100)
        {
            return string.Join(' ', new[] { Tens[value / 10], NumberToWords(value % 10) }
                .Where(part => part != "zero"));
        }

        var units = new (long Size, string Name)[]
        {
            (100_000_000_000L, "kharab"),
            (1_000_000_000L, "arab"),
            (10_000_000L, "crore"),
            (100_000L, "lakh"),
            (1_000L, "thousand"),
            (100L, "hundred")
        };
        foreach (var (size, name) in units)
        {
            if (value >= size)
            {
                var count = value / size;
                var remainder = value % size;
                return remainder == 0
                    ? $"{NumberToWords(count)} {name}"
                    : $"{NumberToWords(count)} {name} {NumberToWords(remainder)}";
            }
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}
