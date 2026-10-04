using System.Text.Json;

namespace PharmaBill.CloudSync;

// JSON-identical to PharmaBill.Sync.SyncChangeEnvelope (camelCase), plus optional sharing.
public sealed record CloudChangeDto(
    Guid ChangeId,
    string Entity,
    Guid EntityId,
    string Operation,
    int SchemaVersion,
    string HlcStamp,
    Guid OriginDeviceId,
    DateTime ChangedAtUtc,
    JsonElement Payload,
    IReadOnlyList<Guid>? SharedWithBranchIds = null);

public sealed record PushRequest(Guid DeviceId, IReadOnlyList<CloudChangeDto> Changes);

public sealed record PushAck(
    int Accepted,
    int Duplicates,
    string ServerHlc,
    IReadOnlyList<Guid> AcknowledgedChangeIds);

public sealed record PullResponse(
    IReadOnlyList<CloudChangeDto> Changes,
    string NextSince,
    bool HasMore,
    string ServerHlc);

public sealed record FileUploadResult(string Sha256, long Length, bool AlreadyExisted);

public sealed record CreateTenantRequest(string Name);

public sealed record CreateBranchRequest(Guid TenantId, string Name);

public sealed record CreatedBranch(Guid TenantId, Guid BranchId, string ApiKey);
