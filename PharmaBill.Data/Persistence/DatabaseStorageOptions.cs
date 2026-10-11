using System.IO;

namespace PharmaBill.Data.Persistence;

public sealed class DatabaseStorageOptions
{
	private readonly ActiveStoreContext? _activeStore;

	public DatabaseStorageOptions(string rootDirectory)
	{
		RootDirectory = Path.GetFullPath(rootDirectory);
	}

	public DatabaseStorageOptions(string rootDirectory, ActiveStoreContext activeStore)
	{
		RootDirectory = Path.GetFullPath(rootDirectory);
		_activeStore = activeStore;
	}

	public string RootDirectory { get; }

	/// <summary>Shared data folder for logs, backups, and the encryption key — survives store switches.</summary>
	public string DataDirectory => Path.Combine(RootDirectory, "data");

	/// <summary>
	/// Active pharmacy database. Prefer per-store path via <see cref="ActiveStoreContext"/>;
	/// tests and legacy callers without a store context use <c>data/pharmabill.db</c>.
	/// </summary>
	public string DatabasePath => _activeStore?.DatabasePath ?? Path.Combine(DataDirectory, "pharmabill.db");

	public string KeyPath => Path.Combine(DataDirectory, "database.key");

	public string PrescriptionsPath => Path.Combine(RootDirectory, "prescriptions");

	public string LicencesPath => Path.Combine(RootDirectory, "licences");

	public string ArchivesPath => Path.Combine(RootDirectory, "archives");

	public string BackupsPath => Path.Combine(DataDirectory, "backups");
}
