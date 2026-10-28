using PharmaBill.Core.Gst;

namespace PharmaBill.App.Services;

/// <summary>Tax details entered for a bill line, and whether to record them on the product.</summary>
public sealed record ProductTaxPromptResult(ProductTaxDetails Details, bool SaveToProduct);

/// <summary>Defaults shown in the tax dialog with where they came from: the HSN's classification and a recorded rate.</summary>
public sealed record ProductTaxHints(HsnSuggestion? Hsn, PharmaBill.Data.Services.RememberedRate? Rate);

public interface IPromptService
{
	string? AskText(string title, string message, string label, string? initialText = null);

	/// <summary>
	/// Asks for a product's HSN code and GST rate. Null when the user chose to enter it later. With
	/// <paramref name="newProduct"/> the values are for a product being created, so they are always saved to it.
	/// </summary>
	ProductTaxPromptResult? AskProductTax(string productName, string? currentHsn, decimal? currentRate, bool currentNilRated, bool newProduct = false, ProductTaxHints? hints = null);
}
