using System.Globalization;

namespace PharmaBill.App.Services;

public static class MoneyFormat
{
	public const string RupeeSign = "₹";

	public static CultureInfo IndianCulture { get; } = new CultureInfo("en-IN", useUserOverride: false);

	public static string Number(decimal amount)
	{
		return amount.ToString("N2", IndianCulture);
	}

	public static string Rupees(decimal amount)
	{
		return "₹" + Number(amount);
	}
}
