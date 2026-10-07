using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class SyncFolderSettingsStore(DatabaseStorageOptions storage)
{
	private readonly string _settingsPath = Path.Combine(storage.RootDirectory, "sync-folder-settings.dat");

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public async Task<SyncFolderSettings> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("Sync folder settings are protected with Windows DPAPI.");
			}
			if (!File.Exists(_settingsPath))
			{
				return SyncFolderSettings.Default;
			}
			try
			{
				return JsonSerializer.Deserialize<SyncFolderSettings>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(_settingsPath, cancellationToken), null, DataProtectionScope.CurrentUser)) ?? throw new InvalidDataException("The protected sync folder settings are empty.");
			}
			catch (Exception ex) when ((ex is CryptographicException || ex is JsonException) ? true : false)
			{
				throw new InvalidOperationException("The protected sync folder settings could not be read.", ex);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task SaveAsync(SyncFolderSettings settings, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
		ArgumentException.ThrowIfNullOrWhiteSpace(settings.FolderPath, "settings.FolderPath");
		bool flag;
		switch (settings.Status)
		{
		case "Idle":
		case "Syncing":
		case "Error":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new ArgumentOutOfRangeException("settings", "Sync status must be Idle, Syncing or Error.");
		}
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("Sync folder settings are protected with Windows DPAPI.");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
			byte[] array = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings), null, DataProtectionScope.CurrentUser);
			string temporary = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";
			try
			{
				await using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
				{
					await stream.WriteAsync(array, cancellationToken);
					await stream.FlushAsync(cancellationToken);
				}
				if (File.Exists(_settingsPath))
				{
					File.Replace(temporary, _settingsPath, null);
				}
				else
				{
					File.Move(temporary, _settingsPath);
				}
			}
			finally
			{
				if (File.Exists(temporary))
				{
					File.Delete(temporary);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public static string FindDefaultFolder()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string text = new string[6]
		{
			Environment.GetEnvironmentVariable("GoogleDrive"),
			Environment.GetEnvironmentVariable("GOOGLE_DRIVE"),
			Path.Combine(folderPath, "Google Drive", "My Drive"),
			Path.Combine(folderPath, "GoogleDrive", "My Drive"),
			Path.Combine(folderPath, "Google Drive"),
			Path.Combine(folderPath, "GoogleDrive")
		}.FirstOrDefault((string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
		if (text == null)
		{
			text = Path.Combine(folderPath, "Google Drive");
		}
		return Path.Combine(text, "PharmaBill_Sync");
	}
}
