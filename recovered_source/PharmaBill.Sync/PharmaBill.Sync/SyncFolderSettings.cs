using System;

namespace PharmaBill.Sync;

public sealed record SyncFolderSettings(string FolderPath, bool AutoSyncEnabled, string Status, DateTime? LastSyncAtUtc, DateTime? LastOutgoingAtUtc, int LastOutgoingPackages, int LastIncomingPackages, int LastAppliedChanges, string? LastError)
{
	public static SyncFolderSettings Default { get; } = new SyncFolderSettings(SyncFolderSettingsStore.FindDefaultFolder(), AutoSyncEnabled: false, "Idle", null, null, 0, 0, 0, null);
}
