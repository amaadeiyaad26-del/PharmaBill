namespace PharmaBill.Core.Ai;

/// <summary>
/// Public product details a chemist can review before saving an unlisted medicine.
/// Purchase rate is intentionally absent so it is always entered locally.
/// </summary>
public sealed record SmartDrugSuggestion(
	string BrandName,
	string? GenericName,
	string? Manufacturer,
	string? PackSizeLabel,
	decimal GstRate,
	string HsnCode,
	decimal? ApproximateMrp);

/// <summary>Online lookup result. <see cref="Message"/> is safe to show in the add-medicine footer.</summary>
public sealed record SmartDrugLookupOutcome(SmartDrugSuggestion? Suggestion, string Message);

public static class SmartDrugLookupPrompt
{
	public const string Caption = "Medicine not found locally. Search Online with AI / Web?";

	public const string FilledNotice = "Filled from online lookup. Check the MRP and purchase rate before saving.";

	public const string UnavailableNotice = "Online lookup was unavailable. Enter the details manually.";
}
