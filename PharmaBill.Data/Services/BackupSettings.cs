using System;

namespace PharmaBill.Data.Services;

public sealed record BackupSettings(string? PrimaryFolder, string? SecondaryFolder, string? Password, bool AutomaticEnabled, DateTime? LastBackupAtUtc);
