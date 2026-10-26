using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PharmaBill.Data.Persistence;

/// <summary>
/// The shop's database runs in write-ahead-log mode: a bill being saved on one counter never makes another counter
/// wait to read, or fail to open the database ("database is locked"). New changes go to pharmabill.db-wal and are
/// folded into pharmabill.db at checkpoints, so anything that copies or replaces the database file checkpoints first
/// or moves the -wal and -shm files with it.
/// </summary>
public static class SqliteWal
{
	/// <summary>Write-ahead log, and fsync at checkpoints only: safe against app crashes, fast for a busy counter.</summary>
	public const string Pragmas = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";

	/// <summary>The files that travel with a database in WAL mode.</summary>
	public static readonly IReadOnlyList<string> CompanionSuffixes = ["-wal", "-shm"];

	public static void Apply(DbConnection connection)
	{
		using DbCommand command = connection.CreateCommand();
		command.CommandText = Pragmas;
		command.ExecuteNonQuery();
	}

	public static async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken = default)
	{
		await using DbCommand command = connection.CreateCommand();
		command.CommandText = Pragmas;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	/// <summary>Folds everything in the -wal file into the database file and empties the log.</summary>
	public static void Checkpoint(SqliteConnection connection)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
		command.ExecuteNonQuery();
	}

	/// <summary>
	/// Leaves the database as one self-contained file (no -wal, rollback journal): for copies that are zipped or
	/// handed on, such as a backup snapshot. Opened by the app again, it goes back to WAL mode.
	/// </summary>
	public static async Task MakeSelfContainedAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
	{
		await using SqliteCommand command = connection.CreateCommand();
		command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode = DELETE;";
		await command.ExecuteNonQueryAsync(cancellationToken);
	}
}

/// <summary>Puts every connection a database context opens into WAL mode (see <see cref="SqliteWal"/>).</summary>
public sealed class SqliteWalInterceptor : DbConnectionInterceptor
{
	public static readonly SqliteWalInterceptor Instance = new();

	public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) => SqliteWal.Apply(connection);

	public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
		SqliteWal.ApplyAsync(connection, cancellationToken);
}
