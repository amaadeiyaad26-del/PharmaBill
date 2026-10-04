namespace PharmaBill.Core.Entities;

public enum SyncState
{
    Pending = 0,
    Synced = 1,
    Conflict = 2
}

public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid DeviceId { get; set; }

    public bool IsDeleted { get; set; }

    public string HlcStamp { get; set; } = string.Empty;

    public SyncState SyncState { get; set; } = SyncState.Pending;
}
