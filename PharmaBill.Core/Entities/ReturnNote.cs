using System;

namespace PharmaBill.Core.Entities;

public sealed class ReturnNote : EntityBase
{
	public string ReturnNo { get; set; } = string.Empty;

	public string SourceType { get; set; } = string.Empty;

	public Guid SourceId { get; set; }

	public DateTime ReturnAtUtc { get; set; } = DateTime.UtcNow;

	public decimal TotalAmount { get; set; }

	public string Reason { get; set; } = string.Empty;

	public string? Notes { get; set; }
}
