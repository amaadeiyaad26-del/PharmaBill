using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;

namespace PharmaBill.Data.Persistence;

/// <summary>The pharmacy database cannot be used right now: it is being replaced, or its file is missing.</summary>
public sealed class DatabaseUnavailableException(string message) : InvalidOperationException(message);

/// <summary>
/// One gate for every use of a pharmacy database file. Ordinary work (a bill, a search, a counter's request) is counted
/// while its connection is open. Replacing the file (restore, a fresh store) is exclusive: it stops new work on that
/// file, waits for the work in flight to finish, clears only that file's connection pools, and only then touches the
/// file. A connection is never opened to a missing file, so an empty database is never created by accident.
/// </summary>
public static class DatabaseMaintenance
{
	private static readonly SemaphoreSlim Exclusive = new(1, 1);
	private static readonly object Sync = new();
	private static readonly Dictionary<string, int> Active = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, string> Held = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, HashSet<string>> PooledConnectionStrings = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConditionalWeakTable<DbConnection, StateChangeEventHandler> Tracked = new();
	private static readonly AsyncLocal<string?> Owner = new();

	/// <summary>How long a restore or a fresh store waits for work in flight before giving up (nothing changed).</summary>
	public static TimeSpan DrainTimeout { get; set; } = TimeSpan.FromSeconds(30);

	private static string? Key(string? dataSource)
	{
		if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:" || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return Path.GetFullPath(dataSource);
	}

	private static bool IsOwner(string key) => string.Equals(Owner.Value, key, StringComparison.OrdinalIgnoreCase);

	/// <summary>Something is replacing this database file right now.</summary>
	public static bool IsHeld(string databasePath)
	{
		string? key = Key(databasePath);
		lock (Sync)
		{
			return key != null && Held.ContainsKey(key);
		}
	}

	/// <summary>Connections to this database file open right now (work in flight).</summary>
	public static int ActiveCount(string databasePath)
	{
		string? key = Key(databasePath);
		lock (Sync)
		{
			return key != null && Active.TryGetValue(key, out int count) ? count : 0;
		}
	}

	public static string MissingMessage(string databasePath) =>
		"The pharmacy database file is missing: " + databasePath + ". Nothing was created in its place, so no records are at risk. " +
		"If a backup is being restored, wait for it to finish; otherwise restore the database from a backup.";

	/// <summary>Before a connection opens: refuses while the file is being replaced, and refuses a missing file.</summary>
	public static void BeforeOpen(string? dataSource)
	{
		string? key = Key(dataSource);
		if (key == null || IsOwner(key))
		{
			return;
		}
		lock (Sync)
		{
			if (Held.TryGetValue(key, out string? reason))
			{
				throw new DatabaseUnavailableException(BusyMessage(reason));
			}
		}
		if (!File.Exists(key))
		{
			throw new DatabaseUnavailableException(MissingMessage(key));
		}
	}

	private static string BusyMessage(string reason) =>
		$"The pharmacy database is unavailable while {reason}. Nothing was saved; try again in a moment.";

	/// <summary>After a connection opens: counted as work in flight until it closes.</summary>
	public static void Track(DbConnection connection)
	{
		string? key = Key(connection.DataSource);
		if (key == null || IsOwner(key) || Tracked.TryGetValue(connection, out StateChangeEventHandler? _))
		{
			return;
		}
		lock (Sync)
		{
			if (Held.TryGetValue(key, out string? reason))
			{
				throw new DatabaseUnavailableException(BusyMessage(reason));
			}
			Active[key] = Active.GetValueOrDefault(key) + 1;
			if (new SqliteConnectionStringBuilder(connection.ConnectionString).Pooling)
			{
				if (!PooledConnectionStrings.TryGetValue(key, out HashSet<string>? strings))
				{
					PooledConnectionStrings[key] = strings = new HashSet<string>(StringComparer.Ordinal);
				}
				strings.Add(connection.ConnectionString);
			}
		}
		StateChangeEventHandler? handler = null;
		handler = (_, change) =>
		{
			if (change.CurrentState == ConnectionState.Closed && Tracked.TryGetValue(connection, out StateChangeEventHandler? _))
			{
				Tracked.Remove(connection);
				connection.StateChange -= handler;
				lock (Sync)
				{
					Active[key] = Math.Max(0, Active.GetValueOrDefault(key) - 1);
				}
			}
		};
		Tracked.Add(connection, handler);
		connection.StateChange += handler;
	}

	/// <summary>Closes the idle pooled connections to this one database (never every pool in the process).</summary>
	public static void ClearPools(string databasePath)
	{
		string? key = Key(databasePath);
		string[] strings;
		lock (Sync)
		{
			strings = key != null && PooledConnectionStrings.TryGetValue(key, out HashSet<string>? known) ? known.ToArray() : [];
		}
		foreach (string connectionString in strings)
		{
			using var connection = new SqliteConnection(connectionString);
			SqliteConnection.ClearPool(connection);
		}
	}

	/// <summary>
	/// Replaces the database file exclusively: new work on it is refused with <paramref name="reason"/>; work already in
	/// flight is waited for (up to <see cref="DrainTimeout"/>, then nothing is changed); its pools are cleared; then
	/// <paramref name="body"/> runs. The body's own connections to the file are allowed.
	/// </summary>
	public static async Task RunExclusiveAsync(string databasePath, string reason, Func<Task> body, CancellationToken cancellationToken = default)
	{
		string key = Key(databasePath) ?? throw new ArgumentException("A database file path is required.", nameof(databasePath));
		await Exclusive.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			lock (Sync)
			{
				Held[key] = reason;
			}
			DateTime deadline = DateTime.UtcNow + DrainTimeout;
			int inFlight;
			while ((inFlight = ActiveCount(key)) > 0)
			{
				if (DateTime.UtcNow > deadline)
				{
					throw new DatabaseUnavailableException(
						$"{inFlight} database {(inFlight == 1 ? "operation is" : "operations are")} still running (a counter may be saving a bill). Nothing was changed; try again when the counters are idle.");
				}
				await Task.Delay(50, cancellationToken).ConfigureAwait(false);
			}
			ClearPools(key);
			Owner.Value = key;
			await body().ConfigureAwait(false);
		}
		finally
		{
			Owner.Value = null;
			lock (Sync)
			{
				Held.Remove(key);
			}
			Exclusive.Release();
		}
	}
}

/// <summary>The marker that a database has existed at a path, so a missing file there is an error, not a first run.</summary>
public static class DatabaseCreation
{
	private static string MarkerPath(string databasePath) => databasePath + ".created";

	public static bool WasCreatedBefore(string databasePath) => File.Exists(MarkerPath(databasePath));

	public static void Mark(string databasePath)
	{
		if (File.Exists(databasePath) && !File.Exists(MarkerPath(databasePath)))
		{
			File.WriteAllText(MarkerPath(databasePath), "A Pharma Remit database exists at this path; if it goes missing, it is not recreated. " + DateTime.UtcNow.ToString("O"));
		}
	}

	/// <summary>Deliberately starting a fresh database here (a new store, or the owner chose to start over).</summary>
	public static void Forget(string databasePath)
	{
		if (File.Exists(MarkerPath(databasePath)))
		{
			File.Delete(MarkerPath(databasePath));
		}
	}
}

/// <summary>The pharmacy database is missing where one existed before; nothing was created in its place.</summary>
public sealed class DatabaseMissingException(string databasePath)
	: InvalidOperationException(DatabaseMaintenance.MissingMessage(databasePath))
{
	public string DatabasePath { get; } = databasePath;
}
