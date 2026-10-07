using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class GoogleDriveSyncSettingsStore(DatabaseStorageOptions storage)
{
	private readonly string _path = Path.Combine(storage.RootDirectory, "google-drive-sync.dat");

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public async Task<GoogleDriveSyncSettings> LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("Google Drive settings are protected with Windows DPAPI.");
			}
			if (!File.Exists(_path))
			{
				return GoogleDriveSyncSettings.Default;
			}
			try
			{
				return Sanitize(JsonSerializer.Deserialize<GoogleDriveSyncSettings>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(_path, cancellationToken), null, DataProtectionScope.CurrentUser)) ?? GoogleDriveSyncSettings.Default);
			}
			catch (Exception ex) when ((ex is CryptographicException || ex is JsonException) ? true : false)
			{
				throw new InvalidOperationException("The protected Google Drive settings could not be read.", ex);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private static GoogleDriveSyncSettings Sanitize(GoogleDriveSyncSettings settings)
	{
		if (!settings.IsConnected)
		{
			return settings with
			{
				Status = (string.IsNullOrWhiteSpace(settings.Status) ? "Not Connected" : settings.Status)
			};
		}
		string text = settings.AccountEmail ?? string.Empty;
		string text2 = settings.Status ?? string.Empty;
		string text3 = settings.RefreshToken ?? string.Empty;
		if (!text.Contains("local-simulated", StringComparison.OrdinalIgnoreCase) && !text.EndsWith("@pharmabill.local", StringComparison.OrdinalIgnoreCase) && !text2.Contains("Simulated", StringComparison.OrdinalIgnoreCase) && !text3.Contains("local-simulated", StringComparison.OrdinalIgnoreCase) && !text3.Contains("simulated", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(text) && text.Contains('@', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(text3))
		{
			return settings;
		}
		return GoogleDriveSyncSettings.Default with
		{
			SyncOnExit = settings.SyncOnExit,
			SyncOnBillSave = settings.SyncOnBillSave
		};
	}

	public async Task SaveAsync(GoogleDriveSyncSettings settings, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(settings, "settings");
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("Google Drive settings are protected with Windows DPAPI.");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(_path));
			byte[] bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(settings), null, DataProtectionScope.CurrentUser);
			string temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
			await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
			File.Move(temporary, _path, overwrite: true);
		}
		finally
		{
			_gate.Release();
		}
	}
}
