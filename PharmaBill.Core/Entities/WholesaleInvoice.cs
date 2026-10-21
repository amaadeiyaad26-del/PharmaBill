using PharmaBill.Core;
using System;

namespace PharmaBill.Core.Entities;

public sealed class WholesaleInvoice : EntityBase
{
	public Guid CustomerId { get; set; }

	public string InvoiceNo { get; set; } = string.Empty;

	public DateTime InvoiceAtUtc { get; set; } = BusinessClock.UtcNow;

	public decimal Subtotal { get; set; }

	public decimal TaxAmount { get; set; }

	public decimal CgstAmount { get; set; }

	public decimal SgstAmount { get; set; }

	public decimal IgstAmount { get; set; }

	public decimal RoundOff { get; set; }

	public decimal DiscountAmount { get; set; }

	public decimal TotalAmount { get; set; }

	public decimal PaidAmount { get; set; }

	public string? PaymentStatus { get; set; }

	public string? TransportDetails { get; set; }

	public string? VehicleNumber { get; set; }

	public string? EWayBillNumber { get; set; }

	public string? Irn { get; set; }

	public string Status { get; set; } = "Posted";

	public DateTime? CancelledAtUtc { get; set; }

	public string? CancellationReason { get; set; }

	public string? Notes { get; set; }

	public Guid? BranchId { get; set; }

	/// <summary>Pharmacy's GST state code as resolved when the invoice was saved. Null on invoices saved before it was recorded.</summary>
	public string? PharmacyStateCode { get; set; }

	/// <summary>Buyer's GST state code (place of supply) as resolved when the invoice was saved. Null on older invoices.</summary>
	public string? BuyerStateCode { get; set; }

	/// <summary>
	/// True when the buyer had no valid GSTIN and no state on record, so <see cref="BuyerStateCode"/> is the
	/// pharmacy's own state (place of supply defaults to the supplier's location; sale treated as intra-state).
	/// </summary>
	public bool BuyerStateDefaulted { get; set; }

	/// <summary>Buyer GSTIN as it stood at save; decides B2B vs B2C in returns. Null on older invoices.</summary>
	public string? BuyerGstin { get; set; }

	public string? BuyerLegalName { get; set; }

	public string? BuyerAddress { get; set; }

	/// <summary>JSON <see cref="SellerSnapshot"/> captured at save. Null on invoices saved before snapshots were recorded.</summary>
	public string? SellerSnapshotJson { get; set; }

	/// <summary>JSON <see cref="BuyerSnapshot"/> captured at save.</summary>
	public string? BuyerSnapshotJson { get; set; }
}
