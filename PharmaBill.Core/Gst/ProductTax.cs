using System.Linq;

namespace PharmaBill.Core.Gst;

/// <summary>The HSN code and GST rate a product is billed at, once both are known.</summary>
public sealed record ProductTaxDetails(string HsnCode, decimal GstRate, bool IsNilRated);

/// <summary>
/// Missing is never zero. A product's GST rate is known only when it is above 0%, or when the product is
/// explicitly marked Nil-rated (billed at 0%). A blank rate, or a stored 0% that nobody marked Nil-rated, is
/// "not known" and blocks billing until someone enters it. Nothing here infers a rate or HSN from a name.
/// </summary>
public static class ProductTax
{
	public const string NilRatedLabel = "Nil-rated";

	public static bool HasHsn(string? hsnCode) => !string.IsNullOrWhiteSpace(hsnCode);

	public static bool HasGstRate(decimal? gstRate, bool isNilRated) => isNilRated || gstRate is > 0m;

	public static bool IsComplete(string? hsnCode, decimal? gstRate, bool isNilRated) =>
		HasHsn(hsnCode) && HasGstRate(gstRate, isNilRated);

	/// <summary>The recorded details, or null when the HSN or the rate is not known.</summary>
	public static ProductTaxDetails? Known(string? hsnCode, decimal? gstRate, bool isNilRated) =>
		IsComplete(hsnCode, gstRate, isNilRated)
			? new ProductTaxDetails(hsnCode!.Trim(), isNilRated ? 0m : gstRate!.Value, isNilRated)
			: null;

	/// <summary>"HSN code", "GST rate", "HSN code and GST rate", or null when nothing is missing.</summary>
	public static string? DescribeMissing(string? hsnCode, decimal? gstRate, bool isNilRated)
	{
		bool hsn = !HasHsn(hsnCode);
		bool rate = !HasGstRate(gstRate, isNilRated);
		return (hsn, rate) switch
		{
			(true, true) => "HSN code and GST rate",
			(true, false) => "HSN code",
			(false, true) => "GST rate",
			_ => null
		};
	}

	/// <summary>Validates what a user entered. Both fields are required; 0% is accepted only as Nil-rated.</summary>
	public static bool TryCreate(string? hsnCode, decimal? gstRate, bool isNilRated, out ProductTaxDetails? details, out string? error)
	{
		details = null;
		string hsn = new string((hsnCode ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
		if (hsn.Length == 0)
		{
			error = "Enter the HSN code.";
			return false;
		}

		if (!hsn.All(char.IsAsciiDigit) || hsn.Length is not (4 or 6 or 8))
		{
			error = "HSN code must be 4, 6 or 8 digits.";
			return false;
		}

		if (isNilRated)
		{
			if (gstRate is not (null or 0m))
			{
				error = "A Nil-rated product is billed at 0%. Clear the GST rate or untick Nil-rated.";
				return false;
			}

			details = new ProductTaxDetails(hsn, 0m, true);
			error = null;
			return true;
		}

		if (gstRate is null)
		{
			error = "Enter the GST rate, or tick Nil-rated if this product is billed at 0%.";
			return false;
		}

		if (gstRate.Value == 0m)
		{
			error = "0% must be marked Nil-rated. Tick Nil-rated only if this product is genuinely nil-rated.";
			return false;
		}

		if (gstRate.Value < 0m || gstRate.Value > 100m)
		{
			error = "GST rate must be between 0 and 100.";
			return false;
		}

		details = new ProductTaxDetails(hsn, gstRate.Value, false);
		error = null;
		return true;
	}

	/// <summary>
	/// The tax a bill line is charged at: the product's recorded details, else the details entered on the bill for this
	/// line. Throws when neither is known, so an unknown rate blocks the bill instead of billing at 0%.
	/// </summary>
	public static ProductTaxDetails ForBilling(Entities.Drug drug, ProductTaxDetails? enteredOnBill)
	{
		ProductTaxDetails? recorded = Known(drug.HsnCode, drug.GstRate, drug.IsNilRated);
		if (recorded != null)
		{
			return recorded;
		}

		if (enteredOnBill != null)
		{
			return Require(drug.Name, enteredOnBill.HsnCode, enteredOnBill.IsNilRated ? null : enteredOnBill.GstRate, enteredOnBill.IsNilRated);
		}

		throw new System.InvalidOperationException(
			$"{drug.Name} has no {DescribeMissing(drug.HsnCode, drug.GstRate, drug.IsNilRated)} recorded, so it cannot be billed. "
			+ "Enter it on the bill line, or fix the product under GST & e-Way > Products missing HSN or GST rate.");
	}

	/// <summary>Like <see cref="TryCreate"/>, but throws <see cref="System.InvalidOperationException"/> naming the product.</summary>
	public static ProductTaxDetails Require(string productName, string? hsnCode, decimal? gstRate, bool isNilRated)
	{
		if (!TryCreate(hsnCode, gstRate, isNilRated, out ProductTaxDetails? details, out string? error))
		{
			throw new System.InvalidOperationException(productName + ": " + error);
		}

		return details!;
	}
}
