using System;

namespace PharmaBill.Core.Entities;

public sealed class ExpiryWriteOff : EntityBase
{
	public Guid BatchId { get; set; }

	public Guid DrugId { get; set; }

	public decimal Quantity { get; set; }

	public DateOnly ExpiryDate { get; set; }

	public string Reason { get; set; } = string.Empty;

	public DateTime WrittenOffAtUtc { get; set; } = DateTime.UtcNow;
}
