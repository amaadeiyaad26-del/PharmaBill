namespace PharmaBill.Core.Accounting;

/// <summary>
/// Canonical EntryType values for party ledgers. Keep strings stable for sync and reports.
/// </summary>
public static class LedgerEntryTypes
{
	public const string PurchaseBill = "PurchaseBill";

	/// <summary>Legacy purchase inward type written by older builds.</summary>
	public const string PurchaseInvoiceLegacy = "PurchaseInvoice";

	public const string PurchaseReturn = "PurchaseReturn";

	public const string SupplierPayment = "SupplierPayment";

	public const string WholesaleInvoice = "WholesaleInvoice";

	public const string WholesaleInvoiceCancellation = "WholesaleInvoiceCancellation";

	public const string WholesaleCreditNote = "WholesaleCreditNote";

	public const string WholesaleDebitNote = "WholesaleDebitNote";

	public const string Receipt = "Receipt";

	public const string OnAccountReceipt = "OnAccountReceipt";

	public const string RetailSale = "RetailSale";

	public const string RetailSaleReturn = "RetailSaleReturn";

	public static bool IsPurchaseBill(string? entryType) =>
		string.Equals(entryType, PurchaseBill, StringComparison.OrdinalIgnoreCase)
		|| string.Equals(entryType, PurchaseInvoiceLegacy, StringComparison.OrdinalIgnoreCase);
}
