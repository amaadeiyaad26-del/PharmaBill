using System;

namespace PharmaBill.Core.Entities;

/// <summary>
/// Part of a supplier payment that the pharmacist pointed at a particular purchase bill. Whatever a payment does not
/// point at settles the supplier's oldest dues first.
/// </summary>
public sealed class SupplierPaymentAllocation : EntityBase
{
	/// <summary>The payment's entry in the supplier ledger.</summary>
	public Guid PaymentEntryId { get; set; }

	public Guid PurchaseInvoiceId { get; set; }

	public decimal Amount { get; set; }
}
