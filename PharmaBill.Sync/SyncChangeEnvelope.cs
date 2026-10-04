using System.Text.Json;

namespace PharmaBill.Sync;

public sealed record SyncChangeEnvelope(
    Guid ChangeId,
    string Entity,
    Guid EntityId,
    string Operation,
    int SchemaVersion,
    string HlcStamp,
    Guid OriginDeviceId,
    DateTime ChangedAtUtc,
    JsonElement Payload);

public sealed record SyncPackageManifest(
    int ProtocolVersion,
    Guid PackageId,
    DateTime CreatedAtUtc,
    Guid ExportingDeviceId,
    Guid? IntendedDeviceId,
    string AppVersion,
    int ChangeCount,
    string ManifestSha256,
    IReadOnlyList<SyncPackageEntry> Changes,
    IReadOnlyList<SyncPackageFile> Files);

public sealed record SyncPackageEntry(Guid ChangeId, string Path, long Length, string Sha256);

public sealed record SyncPackageFile(
    string AttachmentId,
    string Entity,
    Guid EntityId,
    string Property,
    string Category,
    string Path,
    long Length,
    string Sha256,
    string MediaType);

public sealed record SyncImportResult(int Applied, int Duplicates, int Conflicts, int StockConflicts);
