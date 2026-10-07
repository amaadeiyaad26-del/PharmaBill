using System;

namespace PharmaBill.Core.Entities;

public sealed class PurchaseInvoice : EntityBase
{
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
}
