using System.IO;

namespace PharmaBill.Data.Persistence;

public sealed class DatabaseStorageOptions(string rootDirectory)
{
	public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

	/// <summary>Persistent data folder (%LocalAppData%\PharmaBill\data) — survives app reinstalls.</summary>
	public string DataDirectory => Path.Combine(RootDirectory, "data");

	public string DatabasePath => Path.Combine(DataDirectory, "pharmabill.db");

	public string KeyPath => Path.Combine(DataDirectory, "database.key");

	public string PrescriptionsPath => Path.Combine(RootDirectory, "prescriptions");

	public string LicencesPath => Path.Combine(RootDirectory, "licences");

	public string ArchivesPath => Path.Combine(RootDirectory, "archives");

	public string BackupsPath => Path.Combine(DataDirectory, "backups");
}
