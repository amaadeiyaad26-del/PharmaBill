using System;

namespace PharmaBill.Core.Entities;

public sealed class ChangeLog : EntityBase
{
	public string EntityName { get; set; } = string.Empty;

	public Guid EntityId { get; set; }

	public string Operation { get; set; } = string.Empty;

	public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

	public string Payload { get; set; } = string.Empty;
}
