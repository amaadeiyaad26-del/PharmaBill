using System;

namespace PharmaBill.Core.Entities;

public abstract class EntityBase
{
	public Guid Id { get; set; } = Guid.NewGuid();

	public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

	public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

	public Guid DeviceId { get; set; }

	public bool IsDeleted { get; set; }

	public string HlcStamp { get; set; } = string.Empty;

	public SyncState SyncState { get; set; }
}
