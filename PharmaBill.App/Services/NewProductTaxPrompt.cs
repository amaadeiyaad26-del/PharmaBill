using PharmaBill.Core.Gst;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>Stock entry that would create a new product must supply its HSN code and GST rate first.</summary>
public static class NewProductTaxPrompt
{
	/// <summary>
	/// Returns <paramref name="input"/> unchanged when the product already exists or already carries valid tax details;
	/// otherwise asks for them (offering the GST % typed for this stock entry) and returns the completed input.
	/// Null when the user cancels, in which case nothing should be saved.
	/// </summary>
	public static async Task<AddStockInput?> CompleteAsync(AddStockService service, IPromptService prompt, AddStockInput input)
	{
		ArgumentNullException.ThrowIfNull(service);
		ArgumentNullException.ThrowIfNull(prompt);
		if (!await service.WouldCreateProductAsync(input.DrugId, input.CatalogMedicineId))
		{
			return input;
		}

		decimal? typedRate = input.GstRate > 0m ? input.GstRate : null;
		if (ProductTax.TryCreate(input.HsnCode, input.IsNilRated ? null : typedRate, input.IsNilRated, out _, out _))
		{
			return input;
		}

		ProductTaxPromptResult? entered = prompt.AskProductTax(input.MedicineName.Trim(), input.HsnCode, typedRate, input.IsNilRated, newProduct: true);
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
