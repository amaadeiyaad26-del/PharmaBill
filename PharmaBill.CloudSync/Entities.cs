namespace PharmaBill.CloudSync.Data;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Device
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public DateTime FirstSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}

public enum ChangeScope
{
    Global = 0,
    Branch = 1,
    Shared = 2
}

public sealed class CloudChange
{
    public long Seq { get; set; }
    public Guid TenantId { get; set; }
    public Guid ChangeId { get; set; }
    public Guid BranchId { get; set; }
    public Guid OriginDeviceId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string HlcStamp { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public ChangeScope Scope { get; set; }
    public string SharedBranchIds { get; set; } = string.Empty;
    public string ServerHlc { get; set; } = string.Empty;
    public long ServerMs { get; set; }
    public long ServerCounter { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class FileBlob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public long Length { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public bool IsShared { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
