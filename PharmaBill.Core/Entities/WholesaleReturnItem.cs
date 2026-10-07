using System;

namespace PharmaBill.Core.Entities;

public sealed class WholesaleReturnItem : EntityBase
{
	public Guid ReturnNoteId { get; set; }

	public Guid WholesaleInvoiceItemId { get; set; }

	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	public decimal Quantity { get; set; }

	public decimal CreditAmount { get; set; }

	public bool Restocked { get; set; }

	public bool Quarantined { get; set; }
}
