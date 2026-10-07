using System;

namespace PharmaBill.Sync;

public sealed record PairingCode(string Code, DateTime ExpiresAtUtc);
