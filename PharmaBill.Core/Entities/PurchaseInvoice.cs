using System;

namespace PharmaBill.Core.Entities;

public sealed class PurchaseInvoice : EntityBase
{
	public const string PostedStatus = "Posted";

	public const string CommittedStatus = "Committed / Inwarded";

	public Guid SupplierId { get; set; }

	public string InvoiceNo { get; set; } = string.Empty;

	public DateOnly InvoiceDate { get; set; }

	public DateOnly? DueDate { get; set; }

	public decimal Subtotal { get; set; }

	public decimal TaxAmount { get; set; }

	public decimal DiscountAmount { get; set; }

	public decimal TotalAmount { get; set; }

	public string? Status { get; set; }

	public string? Notes { get; set; }

	public Guid? StorageLocationId { get; set; }

	public Guid? BranchId { get; set; }

	public string? AttachedInvoicePath { get; set; }

	public string? OriginalFileName { get; set; }
}
