using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PharmaBill.Data.Persistence;

/// <summary>
/// Before an upgrade changes an existing database's schema, a complete copy of it is made next to it, for example
/// PharmaBill_premigration_2026-10-30_1.0.10.db. If the upgrade fails, that copy is put back, so the pharmacy is
/// never left on a half-upgraded database. The last three copies are kept.
/// </summary>
public static class PreMigrationCopy
{
	public const string FilePrefix = "PharmaBill_premigration_";

	public const int CopiesKept = 3;

	// The version each released installer left the database at, by the last migration it shipped with, so the copy
	// says which version the records came from. A schema not listed here is named by its last migration's date.
	private static readonly IReadOnlyDictionary<string, string> ReleasedSchemas = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["20261022135624_SupplierPaymentAllocations"] = "1.0.10",
		["20261015180314_ProductNilRated"] = "1.0.6",
		["20261021095025_PharmacyLogo"] = "1.0.9",
		// Local Market 1.0.11 and 1.0.12 both shipped with this schema.
		["20261029155419_RecordCreditDebitNoteValues"] = "1.0.11-1.0.12",
	};

	/// <summary>The version label in the copy's name: the release that wrote this schema, or "schema-&lt;date&gt;".</summary>
	public static string VersionLabel(string? lastAppliedMigration)
	{
		if (lastAppliedMigration == null)
		{
			return "schema-none";
		}
		if (ReleasedSchemas.TryGetValue(lastAppliedMigration, out string? version))
		{
			return version;
		}
		string stamp = lastAppliedMigration.Length >= 14 ? lastAppliedMigration[..14] : lastAppliedMigration;
		return "schema-" + stamp;
	}

	/// <summary>
	/// Copies the database (everything in it, including changes still in its -wal file) to a new file beside it, checks
	/// the copy opens and holds the same migration history, and removes all but the newest <see cref="CopiesKept"/>
	/// copies. Throws if any of that fails: the upgrade must not start without a good copy.
	/// </summary>
	public static string Create(string connectionString, string? lastAppliedMigration, DateTime? now = null)
	{
		var sourceBuilder = new SqliteConnectionStringBuilder(connectionString) { Mode = SqliteOpenMode.ReadOnly, Pooling = false };
		string databasePath = Path.GetFullPath(sourceBuilder.DataSource);
		string folder = Path.GetDirectoryName(databasePath)!;
		DateTime when = now ?? DateTime.Now;
		string baseName = FilePrefix + when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "_" + VersionLabel(lastAppliedMigration);
		string target = Path.Combine(folder, baseName + ".db");
		for (int attempt = 2; File.Exists(target); attempt++)
		{
			target = Path.Combine(folder, baseName + "_" + attempt + ".db");
		}

		string temporary = target + ".partial";
		try
		{
			var targetBuilder = new SqliteConnectionStringBuilder(connectionString) { DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false };
			using (var source = new SqliteConnection(sourceBuilder.ToString()))
			using (var destination = new SqliteConnection(targetBuilder.ToString()))
			{
				source.Open();
				destination.Open();
				// SQLite's online backup: one consistent state of the database, including what is still in its -wal file.
				source.BackupDatabase(destination);
				// The copy keeps the database's WAL mode (the app opens every database in it, and a read-only open cannot
				// switch modes), with everything folded into the file itself: closing it leaves no -wal beside it.
				SqliteWal.Checkpoint(destination);
			}

			int sourceHistory = MigrationHistoryCount(sourceBuilder.ToString());
			var checkBuilder = new SqliteConnectionStringBuilder(connectionString) { DataSource = temporary, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
			int copyHistory = MigrationHistoryCount(checkBuilder.ToString());
			if (copyHistory != sourceHistory || !IntegrityOk(checkBuilder.ToString()))
			{
				throw new IOException($"The copy at {temporary} did not match the database (history {copyHistory} of {sourceHistory} migrations).");
			}

			File.Move(temporary, target);
			File.SetLastWriteTime(target, when);
		}
		finally
		{
			foreach (string leftover in new[] { temporary, temporary + "-wal", temporary + "-shm", temporary + "-journal" })
			{
				if (File.Exists(leftover))
				{
					File.Delete(leftover);
				}
			}
		}

		Prune(folder);
		return target;
	}

	/// <summary>
	/// Puts the pre-migration copy back in place of the database. The half-upgraded file is not deleted: it is renamed
	/// to "pharmabill.db.failed-upgrade-&lt;time&gt;" beside it, for support. The copy itself stays where it is.
	/// </summary>
	public static void Restore(string copyPath, string databasePath, string connectionString)
	{
		DatabaseMaintenance.ClearPools(databasePath);
		if (File.Exists(databasePath))
		{
			DatabaseFileHealth.Quarantine(databasePath, "failed-upgrade");
		}

		string temporary = databasePath + ".restoring";
		File.Copy(copyPath, temporary, overwrite: true);
		File.Move(temporary, databasePath);

		var checkBuilder = new SqliteConnectionStringBuilder(connectionString) { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
		var copyBuilder = new SqliteConnectionStringBuilder(connectionString) { DataSource = copyPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
		if (MigrationHistoryCount(checkBuilder.ToString()) != MigrationHistoryCount(copyBuilder.ToString()))
		{
			throw new IOException("The restored database does not match its pre-migration copy " + copyPath + ".");
		}
	}

	/// <summary>The pre-migration copies in this folder, newest first.</summary>
	public static IReadOnlyList<string> CopiesIn(string folder) =>
		Directory.Exists(folder)
			? Directory.GetFiles(folder, FilePrefix + "*.db").OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(path => path, StringComparer.Ordinal).ToList()
			: [];

	private static void Prune(string folder)
	{
		foreach (string old in CopiesIn(folder).Skip(CopiesKept))
		{
			try
			{
				File.Delete(old);
			}
			catch (IOException)
			{
				// Removed on a later upgrade.
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	private static int MigrationHistoryCount(string connectionString)
	{
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';";
		if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
		{
			return 0;
		}
		command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\";";
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	private static bool IntegrityOk(string connectionString)
	{
		using var connection = new SqliteConnection(connectionString);
		connection.Open();
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "PRAGMA quick_check;";
		return string.Equals(command.ExecuteScalar() as string, "ok", StringComparison.OrdinalIgnoreCase);
	}
}
