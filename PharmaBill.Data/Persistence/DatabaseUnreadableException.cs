using System;

namespace PharmaBill.Data.Persistence;

/// <summary>
/// The pharmacy database file has content but will not open. Startup shows a recovery screen instead of crashing;
/// the file is never replaced automatically because the cause may be the encryption key, not damage.
/// </summary>
public sealed class DatabaseUnreadableException(string databasePath, string detail, bool newKeyCreatedThisLaunch, bool isUnencryptedSqlite)
	: InvalidOperationException(
		"The pharmacy database could not be opened (" + detail + "). The file was left untouched: " + databasePath)
{
	public string DatabasePath { get; } = databasePath;

	public string Detail { get; } = detail;

	/// <summary>No database.key existed at startup, so a new key was made: the file is almost certainly fine and needs its original key.</summary>
	public bool NewKeyCreatedThisLaunch { get; } = newKeyCreatedThisLaunch;

	public bool IsUnencryptedSqlite { get; } = isUnencryptedSqlite;
}
