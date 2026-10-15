using System;
using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Services;

/// <summary>
/// GST state codes for a saved wholesale invoice. Invoices record both codes at save time and reports
/// must read those; invoices saved before codes were recorded fall back to the caller's legacy
/// resolution and are flagged as unverified instead of being silently recomputed.
/// </summary>
internal readonly record struct RecordedGstStates(string PharmacyStateCode, string BuyerStateCode, bool Recorded)
{
	public static RecordedGstStates For(WholesaleInvoice invoice, Func<string> legacyPharmacy, Func<string> legacyBuyer)
	{
		if (!string.IsNullOrWhiteSpace(invoice.PharmacyStateCode) && !string.IsNullOrWhiteSpace(invoice.BuyerStateCode))
		{
			return new RecordedGstStates(invoice.PharmacyStateCode, invoice.BuyerStateCode, Recorded: true);
		}
		return new RecordedGstStates(legacyPharmacy(), legacyBuyer(), Recorded: false);
	}

	public static string UnverifiedWarning(string invoiceNo) =>
		"Invoice " + invoiceNo + " was saved before GST state codes were recorded; its state codes were re-resolved from current master data and are UNVERIFIED. Check them before filing.";
}
