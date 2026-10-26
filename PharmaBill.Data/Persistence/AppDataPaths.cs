
namespace PharmaBill.Data.Persistence;

/// <summary>
/// Where the pharmacy's data lives. <see cref="LocalRootDirectory"/> is %LocalAppData%\PharmaBill (logs, the
/// data-location pointer, licence state). <see cref="RootDirectory"/> holds the records (databases, key, backups,
/// attachments, documents): the same folder for the Local Market edition, which survives uninstall; for the Store
/// edition a folder the user chose (see <see cref="StoreDataLocation"/>), because Windows deletes a Store package's
/// app-data folder on uninstall.
/// </summary>
public static class AppDataPaths
{
	public static string LocalRootDirectory { get; } = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		RootFolderName);

	/// <summary>The data root. Defaults to <see cref="LocalRootDirectory"/>; set once at startup by <see cref="UseDataRoot"/>.</summary>
	public static string RootDirectory { get; private set; } = LocalRootDirectory;

	/// <summary>Points every data path at <paramref name="root"/>. Call before anything opens a database or file.</summary>
	public static void UseDataRoot(string root)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(root);
		RootDirectory = Path.GetFullPath(root);
	}

	/// <summary>
	/// Debug builds keep their databases, key, store pointer and logs in a separate folder so a development
	/// build (with half-finished migrations) can never open or alter the real pharmacy database.
	/// </summary>
#if DEBUG
	public const string RootFolderName = "PharmaBill-Debug";
#else
	public const string RootFolderName = "PharmaBill";
#endif

	public static string DataDirectory => Path.Combine(RootDirectory, "data");

	/// <summary>Logs stay on this PC, next to the data-location pointer, whatever the data root is.</summary>
	public static string LogsDirectory => Path.Combine(LocalRootDirectory, "data", "logs");

	public static string BackupsDirectory => Path.Combine(DataDirectory, "backups");

	public static string ConfigDirectory => DataDirectory;

	public static string DatabaseFileName => "pharmabill.db";

	/// <summary>
	/// Legacy shared database path (<c>data\pharmabill.db</c>). Runtime storage prefers
	/// <see cref="ActiveStoreContext.DatabasePath"/> under <c>{SanitizedStoreName}\pharmabill.db</c>.
	/// </summary>
	public static string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);

	public static string DatabaseKeyPath => Path.Combine(DataDirectory, "database.key");

	/// <summary>
	/// Ensures PharmaBill AppData folders exist and migrates a legacy root-level database/key into data\.
	/// </summary>
	public static void EnsureCreatedAndMigrateLegacyLayout()
	{
		Directory.CreateDirectory(RootDirectory);
		Directory.CreateDirectory(DataDirectory);
		Directory.CreateDirectory(LogsDirectory);
		Directory.CreateDirectory(BackupsDirectory);

		MigrateIfNeeded(Path.Combine(RootDirectory, DatabaseFileName), DatabasePath);
		MigrateIfNeeded(Path.Combine(RootDirectory, "database.key"), DatabaseKeyPath);
		MigrateIfNeeded(Path.Combine(RootDirectory, "logs"), null);

		MigrateIfNeeded(Path.Combine(RootDirectory, DatabaseFileName + "-wal"), DatabasePath + "-wal");
		MigrateIfNeeded(Path.Combine(RootDirectory, DatabaseFileName + "-shm"), DatabasePath + "-shm");

		// First-run: copy packaged seed template (if present) before SQLCipher/EF open the path.
		EnsureSeedDatabaseIfMissing(DatabasePath);

		// Move legacy root logs folder files into data\logs when present.
		try
		{
			string legacyLogs = Path.Combine(RootDirectory, "logs");
			if (Directory.Exists(legacyLogs))
			{
				foreach (string file in Directory.EnumerateFiles(legacyLogs))
				{
					string dest = Path.Combine(LogsDirectory, Path.GetFileName(file));
					if (!File.Exists(dest))
					{
						File.Move(file, dest);
					}
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>
	/// Ensures the database directory exists and, when the destination file is missing, copies a packaged
	/// seed template from the install layout (Assets\Seed\pharmabill.db). Removes zero-byte stubs that
	/// would otherwise cause SQLite Error 26 (file is not a database) on first open.
	/// </summary>
	public static void EnsureSeedDatabaseIfMissing(string databasePath)
	{
		if (string.IsNullOrWhiteSpace(databasePath))
		{
			return;
		}

		string? directory = Path.GetDirectoryName(databasePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		try
		{
			if (File.Exists(databasePath) && new FileInfo(databasePath).Length == 0)
			{
				// Moved aside, never deleted (it holds no records); migrations then create a fresh database.
				DatabaseFileHealth.Quarantine(databasePath, "empty");
			}
		}
		catch
		{
			// Best effort; DbInitializer checks the file again before opening it.
		}

		if (File.Exists(databasePath))
		{
			return;
		}

		foreach (string seedPath in GetPackagedSeedCandidates())
		{
			if (!File.Exists(seedPath))
			{
				continue;
			}

			try
			{
				File.Copy(seedPath, databasePath, overwrite: false);
				return;
			}
			catch
			{
				// Try the next candidate; EF migrations create a fresh encrypted DB if none copy.
			}
		}
	}

	private static IEnumerable<string> GetPackagedSeedCandidates()
	{
		string baseDir = AppContext.BaseDirectory;
		yield return Path.Combine(baseDir, "Assets", "Seed", DatabaseFileName);
		yield return Path.Combine(baseDir, "Seed", DatabaseFileName);
		yield return Path.Combine(baseDir, "pharmabill.seed.db");
	}

	private static void MigrateIfNeeded(string legacyPath, string? targetPath)
	{
		if (targetPath == null)
		{
			return;
		}

		try
		{
			if (!File.Exists(legacyPath) || File.Exists(targetPath))
			{
				return;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
			File.Move(legacyPath, targetPath);
		}
		catch
		{
		}
	}
}