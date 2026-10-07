using System;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Sync;

public sealed class GoogleDriveCloudStorageAdapter : ICloudStorageService
{
	private readonly GoogleDriveSyncService _inner;

	public string ProviderId => "google-drive";

	public string DisplayName => "Google Drive";

	public event Action<CloudAccountInfo>? AccountChanged;

	public event Action<CloudSyncProgress>? ProgressChanged;

	public GoogleDriveCloudStorageAdapter(GoogleDriveSyncService inner)
	{
		_inner = inner;
		_inner.SettingsChanged += (GoogleDriveSyncSettings settings) =>
		{
			AccountChanged?.Invoke(ToAccountInfo(settings));
		};
		_inner.ProgressChanged += (GoogleDriveProgress progress) =>
		{
			ProgressChanged?.Invoke(new CloudSyncProgress(ProviderId, progress.Percent, progress.Message));
		};
	}

	public async Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			await _inner.ConnectAsync(string.Empty, cancellationToken);
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	public Task SignOutAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return _inner.DisconnectAsync(cancellationToken);
	}

	public async Task<bool> UploadBackupAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken = default(CancellationToken))
	{
		await _inner.UploadLocalFileAsync(localFilePath, remoteFileName, cancellationToken);
		return true;
	}

	public async Task<string?> DownloadLatestBackupAsync(string localDestDirectory, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await _inner.DownloadLatestFileAsync(localDestDirectory, "PharmaBill-cloud-latest.pbbak", cancellationToken);
	}

	public async Task<CloudAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return ToAccountInfo(await _inner.GetSettingsAsync(cancellationToken));
	}

	private static CloudAccountInfo ToAccountInfo(GoogleDriveSyncSettings settings)
	{
		return new CloudAccountInfo("google-drive", "Google Drive", settings.IsConnected, string.IsNullOrWhiteSpace(settings.AccountEmail) ? null : settings.AccountEmail, settings.Status, settings.LastSyncAtUtc, settings.LastError);
	}
}
