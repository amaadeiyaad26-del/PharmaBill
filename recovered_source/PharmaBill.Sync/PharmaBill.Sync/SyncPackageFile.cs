using System;

namespace PharmaBill.Sync;

public sealed record SyncPackageFile(string AttachmentId, string Entity, Guid EntityId, string Property, string Category, string Path, long Length, string Sha256, string MediaType);
