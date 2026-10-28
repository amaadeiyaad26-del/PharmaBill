using PharmaBill.Core.Gst;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>
/// Stock entry that would create a new product needs its HSN code and GST rate. The HSN comes from what the product is
/// (its dosage form: a classification); the rate comes from the stock form or the supplier's invoice. Only when one of
/// them is still unknown is the user asked, with the classified HSN and the last recorded rate ("same as last time",
/// dated) filled in and labelled.
/// </summary>
public static class NewProductTaxPrompt
{
	/// <summary>
	/// Returns <paramref name="input"/> (with the classified HSN filled in) when nothing needs asking; otherwise asks and
	/// returns the completed input. Null when the user cancels, in which case nothing should be saved.
	/// </summary>
	public static async Task<AddStockInput?> CompleteAsync(AddStockService service, IPromptService prompt, AddStockInput input)
	{
		ArgumentNullException.ThrowIfNull(service);
		ArgumentNullException.ThrowIfNull(prompt);
		if (!await service.WouldCreateProductAsync(input.DrugId, input.CatalogMedicineId))
		{
			return input;
		}

		HsnSuggestion? classified = ProductTax.HasHsn(input.HsnCode) ? null : await service.ClassifyHsnAsync(input.DrugId, input.CatalogMedicineId, input.MedicineName);
		string? hsn = ProductTax.HasHsn(input.HsnCode) ? input.HsnCode!.Trim() : classified?.Code;
		decimal? typedRate = input.GstRate > 0m ? input.GstRate : null;
		if (ProductTax.TryCreate(hsn, input.IsNilRated ? null : typedRate, input.IsNilRated, out _, out _))
		{
			return input with { HsnCode = hsn };
		}

		RememberedRate? remembered = typedRate == null && !input.IsNilRated
			? await service.LastRecordedRateAsync(input.DrugId, input.CatalogMedicineId)
			: null;
		ProductTaxPromptResult? entered = prompt.AskProductTax(input.MedicineName.Trim(), input.HsnCode, typedRate, input.IsNilRated, newProduct: true,
			new ProductTaxHints(classified, remembered));
		if (entered == null)
		{
			return null;
		}

		return input with
		{
			HsnCode = entered.Details.HsnCode,
			GstRate = entered.Details.GstRate,
			IsNilRated = entered.Details.IsNilRated
		};
	}
}
