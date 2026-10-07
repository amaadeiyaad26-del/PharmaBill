using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PharmaBill.Core;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class DataRetentionSettingsStore(DatabaseStorageOptions storage)
{
	private readonly string _settingsPath = Path.Combine(storage.RootDirectory, "data-retention-settings.dat");

	public DataRetentionSettings Load()
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Data retention settings are protected with Windows DPAPI.");
		}
		if (!File.Exists(_settingsPath))
		{
			return new DataRetentionSettings(DataRetentionPeriod.FiveYears, null, null, 0L);
		}
		byte[] encryptedData = File.ReadAllBytes(_settingsPath);
		try
		{
			return JsonSerializer.Deserialize<DataRetentionSettings>(ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser)) ?? new DataRetentionSettings(DataRetentionPeriod.FiveYears, null, null, 0L);
		}
		catch (CryptographicException innerException)
		{
			throw new InvalidOperationException("Data retention settings could not be decrypted for this Windows user.", innerException);
		}
	}

	public void Save(DataRetentionSettings settings)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Data retention settings are protected with Windows DPAPI.");
		}
		ArgumentNullException.ThrowIfNull(settings, "settings");
		Directory.CreateDirectory(storage.RootDirectory);
		byte[] array = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings), null, DataProtectionScope.CurrentUser);
		string text = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(_settingsPath))
			{
				File.Replace(text, _settingsPath, null);
			}
			else
			{
				File.Move(text, _settingsPath);
			}
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}
}
