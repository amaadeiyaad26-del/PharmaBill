using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PharmaBill.Data.Persistence;

public enum DataLocationStatus
{
	/// <summary>No folder chosen yet (first run of the Store edition, or the first run after it was reinstalled).</summary>
	NotChosen,

	Ready,

	/// <summary>The chosen folder is gone (deleted, renamed, or on a drive that is not connected).</summary>
	FolderMissing,

	/// <summary>The folder exists but cannot be read, or is not a Pharma Remit data folder.</summary>
	FolderUnreadable,

	/// <summary>The folder held a database before and no longer does.</summary>
	DatabaseMissing
}

public sealed record DataLocationCheck(DataLocationStatus Status, string? DataRoot, string Message);

public enum DataLocationOutcome
{
	/// <summary>A new, empty data folder (a brand-new install).</summary>
	NewEmpty,

	/// <summary>The folder already held this pharmacy's data (for example after a reinstall); it is used as it is.</summary>
	AttachedExisting,

	/// <summary>Data from the Store package's own folder was copied there, verified, and is now used.</summary>
	MigratedFromPackage
}

public sealed record DataLocationResult(string DataRoot, DataLocationOutcome Outcome, int FilesCopied);

/// <summary>A folder kept in sync by a cloud service.</summary>
public sealed record CloudSyncRoot(string Provider, string Root);

/// <summary>
/// Durable storage for the Store edition. Windows deletes a Store package's app-data folder on uninstall, which
/// would take the billing history and the Schedule H register with it, so the records live in a folder the user
/// chooses (by default %UserProfile%\Pharma Remit Data). A full-trust app may write there without any restricted
/// capability; the choice is remembered in a small pointer file in the package's own folder.
/// A missing or unreadable folder stops startup: the app never quietly opens on an empty database.
/// </summary>
public static class StoreDataLocation
{
	public const string PointerFileName = "data-location.json";

	public const string MarkerFileName = "Pharma Remit data.json";

	private const string MovedNoteFileName = "DATA MOVED - see data-location.json.txt";

	private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

	/// <summary>
	/// %UserProfile%\Pharma Remit Data (…Data Debug for development builds): inside the user's profile, so a shop
	/// assistant without administrator rights can create it, and outside Documents, which Windows often redirects into
	/// OneDrive.
	/// </summary>
	public static string DefaultFolder(string? userProfile = null) => Path.Combine(
		userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
		AppDataPaths.RootFolderName.EndsWith("-Debug", StringComparison.Ordinal) ? "Pharma Remit Data Debug" : "Pharma Remit Data");

	/// <summary>
	/// The cloud service syncing this folder (OneDrive, Google Drive or Dropbox), or null. A live database in a syncing
	/// folder can be locked mid-write, made online-only, or split into conflicting copies, so the user is warned.
	/// Pass <paramref name="roots"/> to check against given sync roots only (tests).
	/// </summary>
	public static string? CloudSyncProvider(string folder, IEnumerable<CloudSyncRoot>? roots = null)
	{
		string full = Path.GetFullPath(folder);
		foreach (CloudSyncRoot root in roots ?? KnownCloudSyncRoots())
		{
			if (!string.IsNullOrWhiteSpace(root.Root) && IsSameOrInside(full, root.Root))
			{
				return root.Provider;
			}
		}

		if (roots == null)
		{
			// Google Drive for desktop streams files through a virtual drive (usually G:) labelled "Google Drive".
			try
			{
				string? drive = Path.GetPathRoot(full);
				if (!string.IsNullOrEmpty(drive) && !drive.StartsWith(@"\\", StringComparison.Ordinal)
					&& new DriveInfo(drive) is { IsReady: true } info
					&& info.VolumeLabel.Equals("Google Drive", StringComparison.OrdinalIgnoreCase))
				{
					return "Google Drive";
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
			{
			}
		}

		return null;
	}

	public static bool IsCloudSynced(string folder, IEnumerable<CloudSyncRoot>? roots = null) => CloudSyncProvider(folder, roots) != null;

	/// <summary>Where OneDrive, Google Drive and Dropbox keep synced folders for this Windows user.</summary>
	public static IReadOnlyList<CloudSyncRoot> KnownCloudSyncRoots()
	{
		List<CloudSyncRoot> roots = new List<CloudSyncRoot>();
		foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
		{
			string? path = Environment.GetEnvironmentVariable(variable);
			if (!string.IsNullOrWhiteSpace(path))
			{
				roots.Add(new CloudSyncRoot("OneDrive", path));
			}
		}

		string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (!string.IsNullOrEmpty(profile))
		{
			// Drive for desktop in mirror mode, and the older Backup and Sync.
			roots.Add(new CloudSyncRoot("Google Drive", Path.Combine(profile, "My Drive")));
			roots.Add(new CloudSyncRoot("Google Drive", Path.Combine(profile, "Google Drive")));
			roots.Add(new CloudSyncRoot("Dropbox", Path.Combine(profile, "Dropbox")));
		}

		// Dropbox records its real folders (personal and business), which may be anywhere, in info.json.
		foreach (Environment.SpecialFolder appData in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData })
		{
			string info = Path.Combine(Environment.GetFolderPath(appData), "Dropbox", "info.json");
			try
			{
				if (File.Exists(info))
				{
					using JsonDocument document = JsonDocument.Parse(File.ReadAllText(info));
					foreach (JsonProperty account in document.RootElement.EnumerateObject())
					{
						if (account.Value.ValueKind == JsonValueKind.Object && account.Value.TryGetProperty("path", out JsonElement path) && path.ValueKind == JsonValueKind.String)
						{
							roots.Add(new CloudSyncRoot("Dropbox", path.GetString()!));
						}
					}
				}
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
			{
			}
		}

		return roots;
	}
	public static DataLocationCheck Check(string localRoot)
	{
		string pointer = Path.Combine(localRoot, PointerFileName);
		if (!File.Exists(pointer))
		{
			return new DataLocationCheck(DataLocationStatus.NotChosen, null, "Choose where to keep your pharmacy data.");
		}

		string? root;
		try
		{
			root = JsonSerializer.Deserialize<Pointer>(File.ReadAllText(pointer))?.DataRoot;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return new DataLocationCheck(DataLocationStatus.FolderUnreadable, null, "The saved data location could not be read: " + ex.Message);
		}

		if (string.IsNullOrWhiteSpace(root))
		{
			return new DataLocationCheck(DataLocationStatus.FolderUnreadable, null, "The saved data location is empty.");
		}

		if (!Directory.Exists(root))
		{
			return new DataLocationCheck(DataLocationStatus.FolderMissing, root,
				"Your pharmacy data folder was not found: " + root + ". If it is on a USB or network drive, connect it; if it was moved, choose its new location.");
		}

		Marker? marker;
		try
		{
			string markerPath = Path.Combine(root, MarkerFileName);
			marker = File.Exists(markerPath) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(markerPath)) : null;
			_ = Directory.EnumerateFileSystemEntries(root).FirstOrDefault();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
		{
			return new DataLocationCheck(DataLocationStatus.FolderUnreadable, root, "Your pharmacy data folder could not be read (" + ex.Message + "): " + root);
		}

		if (marker == null)
		{
			return new DataLocationCheck(DataLocationStatus.FolderUnreadable, root,
				"The folder " + root + " is not a Pharma Remit data folder (" + MarkerFileName + " is missing).");
		}

		if (marker.HasDatabase && !ContainsDatabase(root))
		{
			return new DataLocationCheck(DataLocationStatus.DatabaseMissing, root,
				"Your pharmacy database is no longer in " + root + ". Restore the folder from a backup, or choose where it is now.");
		}

		return new DataLocationCheck(DataLocationStatus.Ready, root, "Using " + root);
	}

	/// <summary>
	/// Records <paramref name="chosenFolder"/> as the data folder. If the package's own folder
	/// (<paramref name="localRoot"/>) already holds data, it is copied there first and verified (every file byte-for-byte,
	/// every database opened with its key) before the switch; the original is left in place. A folder that already holds
	/// Pharma Remit data is attached as it is (a reinstall).
	/// </summary>
	public static DataLocationResult Choose(string localRoot, string chosenFolder)
	{
		string target = Path.GetFullPath(chosenFolder);
		string package = Path.GetFullPath(localRoot);
		if (IsSameOrInside(target, package) || IsSameOrInside(package, target))
		{
			throw new InvalidOperationException("Choose a folder outside the app's own data folder, such as " + DefaultFolder() + ".");
		}

		string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		if (!string.IsNullOrEmpty(appData) && IsSameOrInside(target, Path.Combine(appData, "Packages")))
		{
			throw new InvalidOperationException("That folder belongs to an installed app and would be deleted with it. Choose a folder such as " + DefaultFolder() + ".");
		}

		Directory.CreateDirectory(target);
		bool targetHasData = ContainsDatabase(target) || File.Exists(Path.Combine(target, MarkerFileName));
		bool packageHasData = ContainsDatabase(package);
		if (targetHasData && packageHasData)
		{
			throw new InvalidOperationException("That folder already holds Pharma Remit data, and this installation has its own. Choose an empty folder so neither is overwritten.");
		}

		if (targetHasData)
		{
			if (!File.Exists(Path.Combine(target, MarkerFileName)))
			{
				WriteMarker(target, hasDatabase: true);
			}

			WritePointer(package, target);
			return new DataLocationResult(target, DataLocationOutcome.AttachedExisting, 0);
		}

		if (!packageHasData)
		{
			WriteMarker(target, hasDatabase: false);
			WritePointer(package, target);
			return new DataLocationResult(target, DataLocationOutcome.NewEmpty, 0);
		}

		if (Directory.EnumerateFileSystemEntries(target).Any())
		{
			throw new InvalidOperationException("Choose an empty folder to move your existing pharmacy data into.");
		}

		int copied = CopyAndVerify(package, target);
		WriteMarker(target, hasDatabase: true);
		WritePointer(package, target);
		File.WriteAllText(Path.Combine(package, MovedNoteFileName),
			"Pharma Remit now keeps this pharmacy's data in:" + Environment.NewLine + target + Environment.NewLine
			+ "The files here are the copy taken before the move, left untouched." + Environment.NewLine);
		return new DataLocationResult(target, DataLocationOutcome.MigratedFromPackage, copied);
	}

	/// <summary>Repoints to a folder that already holds this pharmacy's data (after a drive letter changed or the folder was moved).</summary>
	public static DataLocationResult Relocate(string localRoot, string existingDataFolder)
	{
		string target = Path.GetFullPath(existingDataFolder);
		if (!Directory.Exists(target) || !(ContainsDatabase(target) || File.Exists(Path.Combine(target, MarkerFileName))))
		{
			throw new InvalidOperationException("That folder does not contain Pharma Remit data. Choose the folder that holds your pharmacy's records.");
		}

		if (!File.Exists(Path.Combine(target, MarkerFileName)))
		{
			WriteMarker(target, hasDatabase: true);
		}

		WritePointer(Path.GetFullPath(localRoot), target);
		return new DataLocationResult(target, DataLocationOutcome.AttachedExisting, 0);
	}

	/// <summary>After a successful start: from now on a missing database in this folder is an error, not a fresh start.</summary>
	public static void MarkHasDatabase(string dataRoot)
	{
		string markerPath = Path.Combine(dataRoot, MarkerFileName);
		if (ContainsDatabase(dataRoot) && File.Exists(markerPath))
		{
			Marker? marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(markerPath));
			if (marker is { HasDatabase: false })
			{
				WriteMarker(dataRoot, hasDatabase: true);
			}
		}
	}

	/// <summary>True when a non-empty pharmabill.db exists in the folder's data\ or a store sub-folder.</summary>
	public static bool ContainsDatabase(string root)
	{
		if (!Directory.Exists(root))
		{
			return false;
		}

		IEnumerable<string> candidates = new[] { Path.Combine(root, "data", AppDataPaths.DatabaseFileName) }
			.Concat(Directory.EnumerateDirectories(root).Select(store => Path.Combine(store, AppDataPaths.DatabaseFileName)));
		return candidates.Any(path => File.Exists(path) && new FileInfo(path).Length > 0);
	}

	private static int CopyAndVerify(string source, string target)
	{
		List<string> files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
			.Where(file => !IsLocalOnly(Path.GetRelativePath(source, file)))
			.ToList();
		try
		{
			SqliteConnection.ClearAllPools();
			foreach (string file in files)
			{
				string destination = Path.Combine(target, Path.GetRelativePath(source, file));
				Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
				File.Copy(file, destination, overwrite: false);
			}

			foreach (string file in files)
			{
				string destination = Path.Combine(target, Path.GetRelativePath(source, file));
				if (!File.Exists(destination) || new FileInfo(destination).Length != new FileInfo(file).Length || !Hash(destination).AsSpan().SequenceEqual(Hash(file)))
				{
					throw new InvalidOperationException("The copy of " + Path.GetRelativePath(source, file) + " does not match the original.");
				}
			}

			RewriteStoredPaths(source, target);
			VerifyDatabasesOpen(target);
			return files.Count;
		}
		catch (Exception ex)
		{
			// The target was empty before: remove only the partial copy. The original is never touched.
			foreach (string entry in Directory.EnumerateFileSystemEntries(target).ToList())
			{
				if (Directory.Exists(entry))
				{
					Directory.Delete(entry, recursive: true);
				}
				else
				{
					File.Delete(entry);
				}
			}

			throw new InvalidOperationException("Your data was not moved, and nothing was changed in its current location. " + ex.Message, ex);
		}
	}

	// Columns that hold absolute paths to files the app saved under its data root.
	private static readonly (string Table, string Column, string Prefix)[] StoredPathColumns =
	[
		("Sales", "PrescriptionFilePath", ""),
		("Prescriptions", "Notes", "DocumentPath="),
		("CustomerLicences", "DocumentPath", ""),
		("LicenceRecords", "DocumentPath", ""),
		("PurchaseInvoices", "AttachedInvoicePath", "")
	];

	/// <summary>
	/// Prescriptions, licence copies and purchase attachments are recorded by absolute path. In the copied databases
	/// (never the originals) a path under the package folder is pointed at the same file in the new folder, and every
	/// such file must exist there, so the records keep their documents after the package folder is deleted.
	/// </summary>
	private static void RewriteStoredPaths(string source, string target)
	{
		string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(Path.Combine(target, "data", "database.key")).GetOrCreateKey());
		string oldPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source)) + "\\";
		string newPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)) + "\\";
		foreach (string database in Directory.EnumerateFiles(target, AppDataPaths.DatabaseFileName, SearchOption.AllDirectories).Where(path => new FileInfo(path).Length > 0))
		{
			using SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Password = password, Pooling = false }.ToString());
			connection.Open();
			using SqliteTransaction transaction = connection.BeginTransaction();
			foreach ((string table, string column, string prefix) in StoredPathColumns)
			{
				if (!ColumnExists(connection, transaction, table, column))
				{
					continue;
				}

				List<(object Id, string Value)> rows = new List<(object, string)>();
				using (SqliteCommand select = connection.CreateCommand())
				{
					select.Transaction = transaction;
					select.CommandText = $"SELECT Id, \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL;";
					using SqliteDataReader reader = select.ExecuteReader();
					while (reader.Read())
					{
						rows.Add((reader.GetValue(0), reader.GetString(1)));
					}
				}

				foreach ((object id, string value) in rows)
				{
					if (!value.StartsWith(prefix + oldPrefix, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					string relative = value.Substring(prefix.Length + oldPrefix.Length);
					string moved = newPrefix + relative;
					if (File.Exists(Path.Combine(source, relative)) && !File.Exists(moved))
					{
						throw new InvalidOperationException("A document recorded in " + table + " was not found in the copy: " + relative);
					}

					using SqliteCommand update = connection.CreateCommand();
					update.Transaction = transaction;
					update.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = $value WHERE Id = $id;";
					update.Parameters.AddWithValue("$value", prefix + moved);
					update.Parameters.AddWithValue("$id", id);
					update.ExecuteNonQuery();
				}
			}

			transaction.Commit();
		}

		SqliteConnection.ClearAllPools();
	}

	private static bool ColumnExists(SqliteConnection connection, SqliteTransaction transaction, string table, string column)
	{
		using SqliteCommand command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = "SELECT count(*) FROM pragma_table_info($table) WHERE name = $column;";
		command.Parameters.AddWithValue("$table", table);
		command.Parameters.AddWithValue("$column", column);
		return Convert.ToInt64(command.ExecuteScalar()) > 0;
	}

	/// <summary>Every copied database must open with the copied key before the app switches to it.</summary>
	private static void VerifyDatabasesOpen(string target)
	{
		string keyPath = Path.Combine(target, "data", "database.key");
		if (!File.Exists(keyPath))
		{
			throw new InvalidOperationException("The database key (data\\database.key) was not found in the copy.");
		}

		string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(keyPath).GetOrCreateKey());
		foreach (string database in Directory.EnumerateFiles(target, AppDataPaths.DatabaseFileName, SearchOption.AllDirectories)
			.Where(path => new FileInfo(path).Length > 0))
		{
			DatabaseFileCheck check = DatabaseFileHealth.Inspect(new SqliteConnectionStringBuilder { DataSource = database, Password = password, Pooling = false }.ToString());
			if (check.State != DatabaseFileState.Readable)
			{
				throw new InvalidOperationException("The copied database " + database + " could not be opened: " + check.Detail);
			}
		}

		SqliteConnection.ClearAllPools();
	}

	/// <summary>Stays with this PC, not with the records: the pointer itself, logs, and licence/trial state.</summary>
	private static bool IsLocalOnly(string relativePath)
	{
		string path = relativePath.Replace('/', '\\');
		return path.Equals(PointerFileName, StringComparison.OrdinalIgnoreCase)
			|| path.Equals(MovedNoteFileName, StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("data\\logs\\", StringComparison.OrdinalIgnoreCase)
			|| path.StartsWith("logs\\", StringComparison.OrdinalIgnoreCase)
			|| path.Equals("data\\license.lic", StringComparison.OrdinalIgnoreCase)
			|| path.Equals("data\\license.state", StringComparison.OrdinalIgnoreCase)
			|| path.Equals("data\\trial.anchor", StringComparison.OrdinalIgnoreCase);
	}

	private static byte[] Hash(string path)
	{
		using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		return SHA256.HashData(stream);
	}

	private static bool IsSameOrInside(string path, string folder)
	{
		string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) + "\\";
		string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + "\\";
		return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
	}

	private static void WritePointer(string localRoot, string dataRoot)
	{
		Directory.CreateDirectory(localRoot);
		string path = Path.Combine(localRoot, PointerFileName);
		string temp = path + ".tmp";
		File.WriteAllText(temp, JsonSerializer.Serialize(new Pointer(dataRoot, DateTime.UtcNow), Json));
		File.Move(temp, path, overwrite: true);
	}

	private static void WriteMarker(string dataRoot, bool hasDatabase) =>
		File.WriteAllText(Path.Combine(dataRoot, MarkerFileName), JsonSerializer.Serialize(new Marker(hasDatabase, Environment.MachineName, DateTime.UtcNow), Json));

	private sealed record Pointer(string DataRoot, DateTime ChosenAtUtc);

	private sealed record Marker(bool HasDatabase, string? Machine, DateTime UpdatedAtUtc);
}
