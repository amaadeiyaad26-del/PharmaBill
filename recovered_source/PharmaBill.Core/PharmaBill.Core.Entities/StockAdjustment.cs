using System;

namespace PharmaBill.Core.Entities;

public sealed class StockAdjustment : EntityBase
{
	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	public decimal QuantityChange { get; set; }

	public string Reason { get; set; } = string.Empty;

	public DateTime AdjustedAtUtc { get; set; } = DateTime.UtcNow;
}
