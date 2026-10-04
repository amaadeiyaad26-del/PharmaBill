using System.Globalization;

namespace PharmaBill.App.Services;

public static class MoneyFormat
{
    public const string RupeeSign = "\u20B9";

    public static CultureInfo IndianCulture { get; } = new CultureInfo("en-IN", useUserOverride: false);

    public static string Number(decimal amount) => amount.ToString("N2", IndianCulture);

    public static string Rupees(decimal amount) => RupeeSign + Number(amount);
}