using System;
using System.IO;

namespace PharmaBill.Data.Persistence;

/// <summary>
/// Stable Win32 application data roots under %LocalAppData%\PharmaBill.
/// Executable updates must never wipe these folders.
/// </summary>
public static class AppDataPaths
{
	public static string RootDirectory { get; } = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"PharmaBill");

	public static string DataDirectory => Path.Combine(RootDirectory, "data");

	/// <summary>Logs under the persistent data folder so reinstalls never wipe them.</summary>
	public static string LogsDirectory => Path.Combine(DataDirectory, "logs");

	public static string BackupsDirectory => Path.Combine(DataDirectory, "backups");

	public static string ConfigDirectory => DataDirectory;

	public static string DatabaseFileName => "pharmabill.db";

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