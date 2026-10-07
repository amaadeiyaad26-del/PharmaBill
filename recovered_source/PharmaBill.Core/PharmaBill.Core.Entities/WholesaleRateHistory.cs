using System;

namespace PharmaBill.Core.Entities;

public sealed class WholesaleRateHistory : EntityBase
{
	public Guid CustomerId { get; set; }

	public Guid DrugId { get; set; }

	public Guid BatchId { get; set; }

	public Guid InvoiceId { get; set; }

	public DateTime SoldAtUtc { get; set; } = DateTime.UtcNow;

	public decimal UnitRate { get; set; }

	public decimal Quantity { get; set; }
}
