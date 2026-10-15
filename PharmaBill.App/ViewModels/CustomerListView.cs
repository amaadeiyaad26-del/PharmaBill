using System;
using System.Linq;

namespace PharmaBill.App.ViewModels;

/// <summary>Which list the Customers page shows: retail patients, wholesale buyers or retail buyers.</summary>
public enum CustomerListView
{
	Patients,
	WholesaleBuyers,
	RetailBuyers,
}

/// <summary>Splits customers between the three lists by their buyer type.</summary>
public static class CustomerListRules
{
	// Retail patients are customers with one of these types; "Retail" is the account made for a patient's credit sale.
	public static readonly string[] PatientTypes = { "Walk-in", "Regular", "Chronic Patient", "Staff", "Admitted/MRD" };

	// Buyers who take stock from the wholesale desk as shops, hospitals and institutions.
	public static readonly string[] RetailBuyerTypes = { "Retailer", "Hospital", "Institution" };

	// Buyers who are themselves wholesale trade or anything not retail. Distributor, wholesaler and stockist are one
	// buyer type, stored as "Distributor" and shown with both common names so users find it.
	public static readonly string[] WholesaleBuyerTypes = { PharmaBill.Core.Wholesale.BuyerTypes.DistributorLabel, "Other" };

	public static bool IsPatientType(string? buyerType)
	{
		string type = buyerType?.Trim() ?? string.Empty;
		return string.Equals(type, "Retail", StringComparison.OrdinalIgnoreCase)
			|| PatientTypes.Any((string patientType) => string.Equals(patientType, type, StringComparison.OrdinalIgnoreCase));
	}

	public static bool IsRetailBuyerType(string? buyerType) =>
		RetailBuyerTypes.Any((string type) => string.Equals(type, buyerType?.Trim(), StringComparison.OrdinalIgnoreCase));

	public static bool Belongs(CustomerListView view, string? buyerType, bool isPatient)
	{
		return view switch
		{
			CustomerListView.Patients => isPatient,
			CustomerListView.RetailBuyers => !isPatient && IsRetailBuyerType(buyerType),
			_ => !isPatient && !IsRetailBuyerType(buyerType),
		};
	}
}
