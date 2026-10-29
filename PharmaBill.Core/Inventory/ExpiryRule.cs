namespace PharmaBill.Core.Inventory;

/// <summary>Why a batch has no expiry date. A batch without an expiry always carries one of these.</summary>
public enum NoExpiryReason
{
	/// <summary>A durable instrument (HSN chapter 90: thermometers, BP monitors, nebulisers, scales): it does not expire.</summary>
	DurableInstrument = 1,

	/// <summary>The pack shows no expiry and the user ticked "No expiry printed on pack" for this line.</summary>
	NotPrintedOnPack = 2
}

/// <summary>
/// Every batch needs an expiry date, so first-expiry-first-out and expiry alerts can work. Two exceptions, each recorded
/// on the batch: durable instruments (HSN chapter 90) do not expire; and anything else may be received without one only
/// when the user ticks that the pack shows none. Dressings, test strips and condoms are not chapter 90 and need a date.
/// </summary>
public static class ExpiryRule
{
	public const string NoExpiryPrintedLabel = "No expiry printed on pack";

	/// <summary>True for HSN chapter 90 (optical, measuring and medical instruments).</summary>
	public static bool IsDurableInstrument(string? hsn)
	{
		string digits = new string((hsn ?? string.Empty).Where(char.IsDigit).ToArray());
		return digits.Length >= 4 && digits.StartsWith("90", StringComparison.Ordinal);
	}

	/// <summary>The recorded reason a line may go without an expiry date; null when it needs one.</summary>
	public static NoExpiryReason? ReasonWithoutExpiry(string? hsn, bool noExpiryPrintedTicked) =>
		IsDurableInstrument(hsn) ? NoExpiryReason.DurableInstrument
		: noExpiryPrintedTicked ? NoExpiryReason.NotPrintedOnPack
		: null;

	/// <summary>
	/// An expiry as typed at the counter: MM/YY, MMYY, M/YY, MM-YY, MM.YY or MM/YYYY. The month's last day, as packs
	/// print only month and year.
	/// </summary>
	public static bool TryParseTyped(string? text, out DateOnly expiry)
	{
		expiry = default;
		string raw = (text ?? string.Empty).Trim();
		System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(raw, @"^(\d{1,2})\s*[/\-.]?\s*(\d{2}|\d{4})$");
		if (!match.Success || (match.Groups[1].Value.Length == 1 && raw.Length == 3 && !raw.Contains('/') && !raw.Contains('-') && !raw.Contains('.')))
		{
			return false;
		}
		int month = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
		int year = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
		if (year < 100)
		{
			year += 2000;
		}
		if (month is < 1 or > 12 || year is < 2000 or > 2099)
		{
			return false;
		}
		expiry = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
		return true;
	}

	public static string Missing(string item) =>
		"'" + item + "' needs an expiry date. If the pack genuinely shows none, tick \"" + NoExpiryPrintedLabel + "\" on that line.";
}
