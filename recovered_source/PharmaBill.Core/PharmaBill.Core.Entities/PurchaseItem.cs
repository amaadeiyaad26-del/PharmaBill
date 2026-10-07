using System;

namespace PharmaBill.Core.Entities;

public sealed class PurchaseItem : EntityBase
{
	public Guid PurchaseInvoiceId { get; set; }

	public Guid DrugId { get; set; }

	public Guid? BatchId { get; set; }

	public decimal Quantity { get; set; }

	public decimal FreeQuantity { get; set; }

	public string BatchNo { get; set; } = string.Empty;

	public DateOnly? ExpiryDate { get; set; }

	public decimal? Mrp { get; set; }

	public decimal? Ptr { get; set; }

	public decimal UnitPrice { get; set; }

	public decimal DiscountAmount { get; set; }

	public decimal TaxRate { get; set; }

	public decimal LineTotal { get; set; }
}
