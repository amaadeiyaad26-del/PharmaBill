using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PharmaBill.Data.Persistence;

public enum DatabaseFileState
{
	/// <summary>No file yet; migrations create a fresh encrypted database.</summary>
	Missing,

	/// <summary>Opens with the current key (or is locked or busy, which is not damage).</summary>
	Readable,

	/// <summary>0 bytes, or only zero bytes: it cannot hold any records, so it is safe to move aside.</summary>
	Empty,

	/// <summary>Has content but SQLite refuses it (SQLITE_NOTADB / SQLITE_CORRUPT). Could be damage or the wrong key.</summary>
	Unreadable
}

public sealed record DatabaseFileCheck(DatabaseFileState State, string Detail, bool IsUnencryptedSqlite = false);

/// <summary>
/// Startup check of the pharmacy database file before EF opens it. Only a file that provably holds no records is
/// ever moved aside automatically. A non-empty file that will not open is reported, never replaced: with SQLCipher
/// the same "file is not a database" error also means "right file, wrong key" (for example a missing or different
/// database.key), and an encrypted file has no readable header to tell the two apart.
/// </summary>
public static class DatabaseFileHealth
{
	private const int SqliteNotADatabase = 26;

	private const int SqliteCorrupt = 11;

	private static readonly byte[] PlainSqliteHeader = "SQLite format 3\0"u8.ToArray();

	public static DatabaseFileCheck Inspect(string connectionString)
	{
		SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder(connectionString);
		string path = builder.DataSource;
		FileInfo file = new FileInfo(path);
		if (!file.Exists)
		{
			return new DatabaseFileCheck(DatabaseFileState.Missing, "No database file yet.");
		}

		if (file.Length == 0)
		{
			return new DatabaseFileCheck(DatabaseFileState.Empty, "The database file is 0 bytes.");
		}

		bool unencrypted;
		try
		{
			if (ContainsOnlyZeroBytes(file))
			{
				return new DatabaseFileCheck(DatabaseFileState.Empty, $"The database file is {file.Length:N0} bytes of zeros.");
			}

			unencrypted = HasPlainSqliteHeader(file);
		}
		catch (IOException ex)
		{
			// Could not read it right now (in use elsewhere); not evidence of damage, so startup carries on as before.
			return new DatabaseFileCheck(DatabaseFileState.Readable, "Not inspected: " + ex.Message);
		}

		builder.Mode = SqliteOpenMode.ReadOnly;
		builder.Pooling = false;
		try
		{
			using SqliteConnection connection = new SqliteConnection(builder.ToString());
			connection.Open();
			using SqliteCommand command = connection.CreateCommand();
			command.CommandText = "SELECT count(*) FROM sqlite_master;";
			command.ExecuteScalar();
			return new DatabaseFileCheck(DatabaseFileState.Readable, "Opens with the current key.");
		}
		catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteNotADatabase or SqliteCorrupt)
		{
			string detail = unencrypted
				? "The file is an unencrypted SQLite database, but Pharma Remit databases are encrypted, so it cannot be opened with the database key."
				: ex.Message;
			return new DatabaseFileCheck(DatabaseFileState.Unreadable, detail, unencrypted);
		}
		catch (SqliteException ex)
		{
			// Locked, busy or cannot-open-read-only is not damage; the normal startup path reports it.
			return new DatabaseFileCheck(DatabaseFileState.Readable, $"Not inspected (SQLite error {ex.SqliteErrorCode}).");
		}
	}

	/// <summary>
	/// Moves the database and its -wal / -shm / -journal companions to "<name>.<reason>-<timestamp>" next to it.
	/// Never deletes anything. Returns the new path of the main file.
	/// </summary>
	public static string Quarantine(string databasePath, string reason, DateTime? now = null)
	{
		SqliteConnection.ClearAllPools();
		string stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss");
		string target = databasePath + "." + reason + "-" + stamp;
		for (int attempt = 2; File.Exists(target); attempt++)
		{
			target = databasePath + "." + reason + "-" + stamp + "-" + attempt;
		}

		File.Move(databasePath, target);
		foreach (string suffix in new[] { "-wal", "-shm", "-journal" })
		{
			if (File.Exists(databasePath + suffix))
			{
				File.Move(databasePath + suffix, target + suffix);
			}
		}

		return target;
	}

	private static FileStream OpenShared(FileInfo file) =>
		new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

	private static bool HasPlainSqliteHeader(FileInfo file)
	{
		using FileStream stream = OpenShared(file);
		byte[] header = new byte[PlainSqliteHeader.Length];
		return stream.Read(header, 0, header.Length) == header.Length && header.SequenceEqual(PlainSqliteHeader);
	}

	private static bool ContainsOnlyZeroBytes(FileInfo file)
	{
		using FileStream stream = OpenShared(file);
		byte[] buffer = new byte[81920];
		int read;
		while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
		{
			for (int i = 0; i < read; i++)
			{
				if (buffer[i] != 0)
				{
					return false;
				}
			}
		}

		return true;
	}
}
