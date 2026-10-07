using System;

namespace PharmaBill.Core.Entities;

public sealed class WholesaleInvoiceItem : EntityBase
{
	public Guid WholesaleInvoiceId { get; set; }

	public Guid DrugId { get; set; }

	public Guid BatchId { get; set; }

	public decimal Quantity { get; set; }

	public decimal FreeQuantity { get; set; }

	public decimal UnitPrice { get; set; }

	public decimal DiscountAmount { get; set; }

	public decimal TaxRate { get; set; }

	public decimal TaxableAmount { get; set; }

	public decimal CgstAmount { get; set; }

	public decimal SgstAmount { get; set; }

	public decimal IgstAmount { get; set; }

	public decimal LineTotal { get; set; }
}
