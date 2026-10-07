namespace PharmaBill.Sync;

public sealed record CloudSyncProgress(string ProviderId, int Percent, string Message);
