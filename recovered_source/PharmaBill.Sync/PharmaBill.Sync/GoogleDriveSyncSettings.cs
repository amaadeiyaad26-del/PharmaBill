using System;

namespace PharmaBill.Sync;

public sealed record GoogleDriveSyncSettings(bool IsConnected, string AccountEmail, string Status, DateTime? LastSyncAtUtc, bool SyncOnExit, bool SyncOnBillSave, string? LastError, string? RefreshToken)
{
	public static GoogleDriveSyncSettings Default { get; } = new GoogleDriveSyncSettings(IsConnected: false, string.Empty, "Not Connected", null, SyncOnExit: true, SyncOnBillSave: true, null, null);
}
