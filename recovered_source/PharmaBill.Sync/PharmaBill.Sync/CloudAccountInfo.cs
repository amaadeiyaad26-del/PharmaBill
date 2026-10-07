using System;

namespace PharmaBill.Sync;

public sealed record CloudAccountInfo(string ProviderId, string DisplayName, bool IsConnected, string? AccountEmail, string Status, DateTime? LastSyncAtUtc, string? LastError);
