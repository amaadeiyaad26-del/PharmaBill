using System;
using System.Collections.Generic;

namespace PharmaBill.Sync;

public sealed record SyncPackageManifest(int ProtocolVersion, Guid PackageId, DateTime CreatedAtUtc, Guid ExportingDeviceId, Guid? IntendedDeviceId, string AppVersion, int ChangeCount, string ManifestSha256, IReadOnlyList<SyncPackageEntry> Changes, IReadOnlyList<SyncPackageFile> Files);
