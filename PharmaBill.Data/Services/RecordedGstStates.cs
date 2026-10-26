using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>
/// Tax-document rule: whatever is printed on a tax invoice or filed in a return is captured on the
/// invoice when it is saved, and every print/report reads it back from there. Rows saved before this
/// was recorded fall back to the caller's legacy (master-data) values and are flagged as unverified.
/// </summary>
internal readonly record struct RecordedGstStates(string PharmacyStateCode, string BuyerStateCode, bool Recorded)
{
	public static bool IsRecorded(WholesaleInvoice invoice) =>
		!string.IsNullOrWhiteSpace(invoice.PharmacyStateCode)
		&& !string.IsNullOrWhiteSpace(invoice.BuyerStateCode)
		&& invoice.SellerSnapshotJson != null
		&& invoice.BuyerSnapshotJson != null;

	/// <summary>Printed party details were captured at save.</summary>
	public static bool HasSnapshot(Sale sale) => sale.SellerSnapshotJson != null && sale.BuyerSnapshotJson != null;

	/// <summary>Printed details and the pharmacy's state code were both captured at save.</summary>
	public static bool IsRecorded(Sale sale) => HasSnapshot(sale) && !string.IsNullOrWhiteSpace(sale.PharmacyStateCode);

	public static RecordedGstStates For(WholesaleInvoice invoice, Func<string> legacyPharmacy, Func<string> legacyBuyer)
	{
		if (IsRecorded(invoice))
		{
			return new RecordedGstStates(invoice.PharmacyStateCode!, invoice.BuyerStateCode!, Recorded: true);
		}
		return new RecordedGstStates(legacyPharmacy(), legacyBuyer(), Recorded: false);
	}

	public static string UnverifiedWarning(string invoiceNo) =>
		"Invoice " + invoiceNo + " was saved before its tax details were recorded; party details and state codes were re-read from current master data and are UNVERIFIED. Check them before filing.";
}

/// <summary>Party details for e-Invoice / e-Way payloads: recorded snapshot, or current master data for older invoices.</summary>
internal sealed record RecordedParties(string? SellerGstin, string SellerLegalName, string? SellerAddress, string? SellerLocation, string? BuyerGstin, string BuyerLegalName, string? BuyerAddress, string? BuyerLocation)
{
	public static RecordedParties For(WholesaleInvoice invoice, PharmacyProfile profile, Customer customer, bool recorded)
	{
		SellerSnapshot? seller = recorded ? TaxDocumentSnapshot.Deserialize<SellerSnapshot>(invoice.SellerSnapshotJson) : null;
		BuyerSnapshot? buyer = recorded ? TaxDocumentSnapshot.Deserialize<BuyerSnapshot>(invoice.BuyerSnapshotJson) : null;
		if (seller != null && buyer != null)
		{
			return new RecordedParties(
				seller.Gstin, seller.LegalName ?? seller.Name, seller.Address, seller.StateName,
				invoice.BuyerGstin, invoice.BuyerLegalName ?? buyer.Name, invoice.BuyerAddress, buyer.StateName);
		}
		return new RecordedParties(
			profile.Gstin, profile.LegalName ?? profile.Name, profile.Address, profile.State,
			customer.Gstin, customer.Name, customer.Address, customer.State);
	}
}

/// <summary>Builds the snapshots recorded on a tax document at save time.</summary>
internal static class TaxDocumentSnapshotBuilder
{
	public static async Task<SellerSnapshot> BuildSellerAsync(PharmaBillDbContext context, PharmacyProfile profile, Branch? branch, string? stateCode, RetailPrintBranding? branding, CancellationToken cancellationToken)
	{
		List<LicenceRecord> licences = await context.LicenceRecords.AsNoTracking()
			.OrderBy(item => item.LicenceType)
			.ThenBy(item => item.LicenceNumber)
			.ToListAsync(cancellationToken);
		return new SellerSnapshot
		{
			Name = profile.Name,
			LegalName = profile.LegalName,
			Address = profile.Address,
			Phone = profile.Phone,
			Gstin = profile.Gstin,
			BranchGstin = branch?.Gstin,
			BranchDrugLicenceNo = branch?.DrugLicenseNo,
			StateCode = stateCode,
			StateName = IndianGstStateCodes.NameForCode(stateCode),
			Licences = licences.Select(item => new LicenceSnapshot(item.LicenceType, item.LicenceNumber, item.ExpiresOn)).ToArray(),
			CompetentPersonName = profile.CompetentPersonName,
			CompetentPersonQualification = profile.CompetentPersonQualification,
			CompetentPersonRegistrationNumber = profile.CompetentPersonRegistrationNumber,
			BankName = profile.BankName,
			BankAccountName = profile.BankAccountName,
			BankIfsc = profile.BankIfsc,
			ExtraLicenceLines = branding?.ExtraLicenceLines ?? Array.Empty<string>(),
			FssaiNumber = branding?.FssaiNumber,
			AddressLine2 = branding?.AddressLine2,
			TermsText = branding?.TermsText
		};
	}
}
