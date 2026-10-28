namespace PharmaBill.Core.Gst;

/// <summary>GST rates as recorded facts: the valid slabs, and the date before which a medicine's recorded rate is stale.</summary>
public static class GstRates
{
	/// <summary>
	/// The September 2025 rate cuts moved most medicines from 12% to 5% (some to nil), effective this date. A rate
	/// recorded before it is never offered as "same as last time".
	/// </summary>
	public static readonly DateOnly MedicineRateCutDate = new(2025, 9, 22);

	/// <summary>Total GST rates that exist for goods. 2.5% and 6% are halves (CGST or SGST alone), not rates.</summary>
	public static readonly IReadOnlyList<decimal> StandardSlabs = [0.1m, 0.25m, 1m, 1.5m, 3m, 5m, 12m, 18m, 28m, 40m];

	public static bool IsStandardSlab(decimal rate) => StandardSlabs.Contains(rate);

	public static bool IsBeforeRateCut(DateOnly recordedOn) => recordedOn < MedicineRateCutDate;
}
