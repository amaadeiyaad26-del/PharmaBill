namespace PharmaBill.Data.Services;

public sealed record BackupRestoreResult(string SafetyBackupPath, BackupManifest Manifest);
