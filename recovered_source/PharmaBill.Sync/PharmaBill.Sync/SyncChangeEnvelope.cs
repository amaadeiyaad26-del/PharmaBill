using System;
using System.Text.Json;

namespace PharmaBill.Sync;

public sealed record SyncChangeEnvelope(Guid ChangeId, string Entity, Guid EntityId, string Operation, int SchemaVersion, string HlcStamp, Guid OriginDeviceId, DateTime ChangedAtUtc, JsonElement Payload);
