using System;

namespace PharmaBill.Core.Entities;

public sealed class StockMovement : EntityBase
{
	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	public Guid? LocationId { get; set; }

	public decimal QuantityChange { get; set; }

	public string MovementType { get; set; } = string.Empty;

	public string? ReferenceType { get; set; }

	public Guid? ReferenceId { get; set; }

	public DateTime MovementAtUtc { get; set; } = DateTime.UtcNow;

	public string? Notes { get; set; }
}
