using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PharmaBill.Sync;

public sealed record CloudChangeWire(Guid ChangeId, string Entity, Guid EntityId, string Operation, int SchemaVersion, string HlcStamp, Guid OriginDeviceId, DateTime ChangedAtUtc, JsonElement Payload, IReadOnlyList<Guid>? SharedWithBranchIds = null);
