using System;

namespace PharmaBill.Core.Entities;

public sealed class PurchaseReturn : EntityBase
{
	public Guid SupplierId { get; set; }

	public string ReturnNo { get; set; } = string.Empty;

	public DateOnly ReturnDate { get; set; }

	public decimal TotalAmount { get; set; }

	public string Reason { get; set; } = string.Empty;
}
