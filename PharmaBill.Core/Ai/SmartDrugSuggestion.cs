namespace PharmaBill.Core.Ai;

/// <summary>
/// Public product details a chemist can review before saving an unlisted medicine.
/// Purchase rate is intentionally absent so it is always entered locally.
/// HSN and GST rate come only from this pharmacy's own product record; online lookups leave them null,
/// because a tax rate must never be guessed from a medicine name.
/// </summary>
public sealed record SmartDrugSuggestion(
	string BrandName,
	string? GenericName,
	string? Manufacturer,
	string? PackSizeLabel,
	decimal? GstRate,
	string? HsnCode,
	decimal? ApproximateMrp,
	bool IsNilRated = false);

/// <summary>Online lookup result. <see cref="Message"/> is safe to show in the add-medicine footer.</summary>
public sealed record SmartDrugLookupOutcome(SmartDrugSuggestion? Suggestion, string Message);

public static class SmartDrugLookupPrompt
{
	public const string Caption = "Not found - add manually";

	public const string FilledNotice = "Filled from online lookup. Enter the HSN code and GST rate from the supplier invoice, and check the MRP and purchase rate before saving.";

	public const string UnavailableNotice = "Gemini limit reached, try again in a minute";
}
