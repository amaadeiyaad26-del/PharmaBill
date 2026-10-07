namespace PharmaBill.Sync;

public sealed record CloudSyncRunResult(int Pushed, int Pulled, int Applied);
