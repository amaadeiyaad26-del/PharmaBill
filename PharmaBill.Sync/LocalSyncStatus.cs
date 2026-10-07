namespace PharmaBill.Sync;

public sealed record LocalSyncStatus(LocalSyncState State, string Message, string? Detail = null);
