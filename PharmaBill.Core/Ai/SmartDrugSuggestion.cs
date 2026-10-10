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
	public const string Caption = "Not found - add manually";

	public const string FilledNotice = "Filled from online lookup. Check the MRP and purchase rate before saving.";

	public const string UnavailableNotice = "Gemini limit reached, try again in a minute";
}
