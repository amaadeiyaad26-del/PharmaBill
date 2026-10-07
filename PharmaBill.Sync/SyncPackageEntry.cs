using System;

namespace PharmaBill.Sync;

public sealed record SyncPackageEntry(Guid ChangeId, string Path, long Length, string Sha256);
