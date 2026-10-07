namespace PharmaBill.Sync;

public sealed record SyncImportResult(int Applied, int Duplicates, int Conflicts, int StockConflicts);
