namespace PharmaBill.Core.Ai;

/// <summary>
/// Public product details a chemist can review before saving an unlisted medicine.
/// Purchase rate is intentionally absent so it is always entered locally.
/// HSN and GST rate come only from this pharmacy's own product record; the medicine data bank leaves them null,
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

/// <summary>Data bank search result. <see cref="Message"/> is safe to show in the add-medicine footer.</summary>
public sealed record SmartDrugLookupOutcome(SmartDrugSuggestion? Suggestion, string Message);

public static class SmartDrugLookupPrompt
{
	public const string Caption = "Not found - add manually";

	public const string ButtonLabel = "🔍 Search data bank";

	public const string FilledNotice = "Filled from the medicine data bank. Enter the HSN code and GST rate from the supplier invoice, and check the MRP and purchase rate before saving.";

	public const string FilledWithTaxNotice = "Filled from your own medicine list. Check the MRP and purchase rate before saving.";

	public static string NotFoundNotice(string typed) =>
		"'" + typed.Trim() + "' is not in the medicine data bank. Check the spelling, or type the details from the pack or the supplier invoice.";
}
