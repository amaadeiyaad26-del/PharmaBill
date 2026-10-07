using System;

namespace PharmaBill.Sync;

public sealed record FileTransferItem(string AttachmentId, string Entity, Guid EntityId, string Property, string LocalPath, string Category, long Length, string Sha256, string MediaType, Guid SourceDeviceId, string State, int RetryCount, string? LastError);
