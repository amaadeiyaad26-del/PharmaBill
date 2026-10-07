using System;

namespace PharmaBill.Core.Entities;

public sealed class SyncConflict : EntityBase
{
	public string EntityName { get; set; } = string.Empty;

	public Guid EntityId { get; set; }

	public string LocalPayload { get; set; } = string.Empty;

	public string RemotePayload { get; set; } = string.Empty;

	public string? Resolution { get; set; }

	public DateTime? ResolvedAtUtc { get; set; }
}
