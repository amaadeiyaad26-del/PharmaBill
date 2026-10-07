using System;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Sync;

public interface ICloudStorageService
{
	string ProviderId { get; }

	string DisplayName { get; }

	event Action<CloudAccountInfo>? AccountChanged;

	event Action<CloudSyncProgress>? ProgressChanged;

	Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task SignOutAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<bool> UploadBackupAsync(string localFilePath, string remoteFileName, CancellationToken cancellationToken = default(CancellationToken));

	Task<string?> DownloadLatestBackupAsync(string localDestDirectory, CancellationToken cancellationToken = default(CancellationToken));

	Task<CloudAccountInfo> GetAccountInfoAsync(CancellationToken cancellationToken = default(CancellationToken));
}
