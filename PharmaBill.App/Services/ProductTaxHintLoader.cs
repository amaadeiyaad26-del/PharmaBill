using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>
/// What the tax dialog offers for a product in the master that is missing its HSN or rate: the HSN its dosage form
/// gives, and the last rate the shop recorded for it or its salt ("same as last time", dated). Both editable.
/// </summary>
public static class ProductTaxHintLoader
{
	public static async Task<ProductTaxHints> LoadAsync(IServiceScopeFactory scopes, Guid drugId, string? currentHsn, decimal? currentRate, bool currentNilRated)
	{
		try
		{
			using IServiceScope scope = scopes.CreateScope();
			ProductTaxService taxes = scope.ServiceProvider.GetRequiredService<ProductTaxService>();
			HsnSuggestion? hsn = ProductTax.HasHsn(currentHsn) ? null : await taxes.ClassifyHsnAsync(drugId, null, null);
			RememberedRate? rate = ProductTax.HasGstRate(currentRate, currentNilRated) ? null : await taxes.LastRecordedRateAsync(drugId, null);
			return new ProductTaxHints(hsn, rate);
		}
		catch (Exception)
		{
			// The dialog still opens; it just has nothing to offer.
			return new ProductTaxHints(null, null);
		}
	}
}
