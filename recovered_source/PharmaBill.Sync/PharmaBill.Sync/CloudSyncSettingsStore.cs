using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class CloudSyncSettingsStore(DatabaseStorageOptions storage)
{
	private readonly string _path = Path.Combine(storage.RootDirectory, "cloud-sync-settings.dat");

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public async Task<CloudSyncSettings> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("Cloud sync settings are protected with Windows DPAPI.");
			}
			if (!File.Exists(_path))
			{
				return CloudSyncSettings.Default;
			}
			try
			{
				return JsonSerializer.Deserialize<CloudSyncSettings>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path, cancellationToken), null, DataProtectionScope.CurrentUser)) ?? throw new InvalidDataException("The cloud sync settings are empty.");
			}
			catch (Exception ex) when ((ex is CryptographicException || ex is JsonException) ? true : false)
			{
				throw new InvalidOperationException("The protected cloud sync settings could not be read.", ex);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task SaveAsync(CloudSyncSettings settings, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
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
			throw new ArgumentOutOfRangeException("settings", "Status must be Idle, Syncing or Error.");
		}
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("Cloud sync settings are protected with Windows DPAPI.");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings), null, DataProtectionScope.CurrentUser);
			string temp = $"{_path}.{Guid.NewGuid():N}.tmp";
			try
			{
				await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
				if (File.Exists(_path))
				{
					File.Replace(temp, _path, null);
				}
				else
				{
					File.Move(temp, _path);
				}
			}
			finally
			{
				if (File.Exists(temp))
				{
					File.Delete(temp);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}
}
