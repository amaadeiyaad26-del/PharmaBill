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

	public static string Missing(string item) =>
		"'" + item + "' needs an expiry date. If the pack genuinely shows none, tick \"" + NoExpiryPrintedLabel + "\" on that line.";
}
