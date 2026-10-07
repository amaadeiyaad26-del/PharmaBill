using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class CloudAutoBackupPreferencesStore(DatabaseStorageOptions storage)
{
	private readonly string _path = Path.Combine(storage.RootDirectory, "cloud-auto-backup.dat");

	public CloudAutoBackupPreferences Load()
	{
		if (!OperatingSystem.IsWindows() || !File.Exists(_path))
		{
			return CloudAutoBackupPreferences.Default;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser));
			if (!jsonDocument.RootElement.TryGetProperty("Destinations", out var value))
			{
				return CloudAutoBackupPreferences.Default;
			}
			int num = ((value.ValueKind == JsonValueKind.Number) ? value.GetInt32() : 0);
			bool flag = (uint)(num - 1) <= 2u;
			return flag ? new CloudAutoBackupPreferences(CloudAutoBackupDestination.GoogleDrive) : CloudAutoBackupPreferences.Default;
		}
		catch
		{
			return CloudAutoBackupPreferences.Default;
		}
	}

	public void Save(CloudAutoBackupPreferences preferences)
	{
		ArgumentNullException.ThrowIfNull(preferences, "preferences");
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Cloud auto-backup preferences use Windows DPAPI.");
		}
		CloudAutoBackupPreferences value = (preferences.IncludesGoogle ? new CloudAutoBackupPreferences(CloudAutoBackupDestination.GoogleDrive) : CloudAutoBackupPreferences.Default);
		Directory.CreateDirectory(storage.RootDirectory);
		byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(value), null, DataProtectionScope.CurrentUser);
		string text = $"{_path}.{Guid.NewGuid():N}.tmp";
		File.WriteAllBytes(text, bytes);
		File.Move(text, _path, overwrite: true);
	}
}
