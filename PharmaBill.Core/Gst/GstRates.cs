namespace PharmaBill.Core.Gst;

/// <summary>GST rates as recorded facts: the valid slabs, and the date before which a medicine's recorded rate is stale.</summary>
public static class GstRates
{
	/// <summary>
	/// The September 2025 rate cuts moved most medicines from 12% to 5% (some to nil), effective this date. A rate
	/// recorded before it is never offered as "same as last time".
	/// </summary>
	public static readonly DateOnly MedicineRateCutDate = new(2025, 9, 22);

	/// <summary>The GST rates a pharmacy bills at (besides 0). 2.5, 6, 9 and 14 are halves (CGST or SGST alone), not rates.</summary>
	public static readonly IReadOnlyList<decimal> StandardSlabs = [5m, 12m, 18m, 28m];

	public const string HalfRateMessage = "Enter the total GST rate, not the CGST half — for 5% GST, enter 5.";

	public const string SlabMessage = "GST rate must be 0, 5, 12, 18 or 28.";

	public static bool IsStandardSlab(decimal rate) => StandardSlabs.Contains(rate);

	/// <summary>Null for 0, 5, 12, 18 or 28; otherwise what to tell the person who typed it.</summary>
	public static string? Problem(decimal rate) =>
		rate == 0m || IsStandardSlab(rate) ? null
		: IsStandardSlab(rate * 2m) ? HalfRateMessage
		: SlabMessage;

	public static bool IsBeforeRateCut(DateOnly recordedOn) => recordedOn < MedicineRateCutDate;
}
