using System;

namespace PharmaBill.Core.Entities;

public sealed class PurchaseReturnItem : EntityBase
{
	public Guid PurchaseReturnId { get; set; }

	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	public decimal Quantity { get; set; }

	public decimal UnitPrice { get; set; }

	public decimal LineTotal { get; set; }
}
