using System;

namespace PharmaBill.Core.Entities;

public sealed class StockVerificationSession : EntityBase
{
	public string SessionNo { get; set; } = string.Empty;

	public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

	public DateTime? PostedAtUtc { get; set; }

	public string Status { get; set; } = "Open";
}
