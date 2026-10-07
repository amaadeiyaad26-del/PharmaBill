using System;

namespace PharmaBill.Core.Entities;

public sealed class StockVerificationItem : EntityBase
{
	public Guid SessionId { get; set; }

	public Guid BatchId { get; set; }

	public decimal ExpectedQuantity { get; set; }

	public decimal CountedQuantity { get; set; }

	public decimal AdjustmentQuantity { get; set; }
}
