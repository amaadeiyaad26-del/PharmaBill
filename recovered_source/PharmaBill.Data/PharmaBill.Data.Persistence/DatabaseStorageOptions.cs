using System.IO;

namespace PharmaBill.Data.Persistence;

public sealed class DatabaseStorageOptions(string rootDirectory)
{
	public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

	public string DatabasePath => Path.Combine(RootDirectory, "pharmabill.db");

	public string KeyPath => Path.Combine(RootDirectory, "database.key");

	public string PrescriptionsPath => Path.Combine(RootDirectory, "prescriptions");

	public string LicencesPath => Path.Combine(RootDirectory, "licences");

	public string ArchivesPath => Path.Combine(RootDirectory, "archives");
}
