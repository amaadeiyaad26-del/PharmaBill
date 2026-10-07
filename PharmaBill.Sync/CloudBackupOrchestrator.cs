using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.Sync;

public sealed class CloudBackupOrchestrator(IEnumerable<ICloudStorageService> providers, CloudAutoBackupPreferencesStore preferences, GoogleDriveSyncService googleDrive, BranchNetworkSyncService branchNetwork, BackupSettingsStore backupSettings, DatabaseStorageOptions storage, IServiceScopeFactory scopeFactory, ILogger<CloudBackupOrchestrator> logger)
{
	public async Task UploadToSelectedProvidersAsync(bool triggeredByBillSave, CancellationToken cancellationToken = default(CancellationToken), bool forceWhenConnected = false)
	{
		CloudAutoBackupPreferences prefs = preferences.Load();
		GoogleDriveSyncSettings googleDriveSyncSettings = await googleDrive.GetSettingsAsync(cancellationToken);
		bool flag = forceWhenConnected || (triggeredByBillSave ? googleDriveSyncSettings.SyncOnBillSave : googleDriveSyncSettings.SyncOnExit);
		if (!googleDriveSyncSettings.IsConnected || !flag || (prefs.Destinations != CloudAutoBackupDestination.None && !prefs.IncludesGoogle))
		{
			return;
		}
		List<ICloudStorageService> targets = providers.Where((ICloudStorageService cloudStorageService) => cloudStorageService.ProviderId == "google-drive").DistinctBy((ICloudStorageService cloudStorageService) => cloudStorageService.ProviderId).ToList();
		if (targets.Count == 0)
		{
			return;
		}
		string password = ResolveBackupPassword();
		string tempBackup = Path.Combine(Path.GetTempPath(), $"PharmaBill-cloud-{Guid.NewGuid():N}.pbbak");
		try
		{
			using (IServiceScope scope = scopeFactory.CreateScope())
			{
				await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>().CreateBackupAsync(tempBackup, password, cancellationToken);
			}
			foreach (ICloudStorageService provider in targets)
			{
				try
				{
					await provider.UploadBackupAsync(tempBackup, "PharmaBill-cloud-latest.pbbak", cancellationToken);
				}
				catch (Exception exception)
				{
					logger.LogWarning(exception, "Cloud upload failed for {Provider}", provider.DisplayName);
				}
			}
			try
			{
				await branchNetwork.PublishCurrentBranchAsync(cancellationToken);
			}
			catch (Exception exception2)
			{
				logger.LogWarning(exception2, "Branch snapshot publish failed.");
			}
		}
		finally
		{
			try
			{
				if (File.Exists(tempBackup))
				{
					File.Delete(tempBackup);
				}
			}
			catch
			{
			}
		}
	}

	private string ResolveBackupPassword()
	{
		string password = backupSettings.Load().Password;
		if (!string.IsNullOrWhiteSpace(password) && password.Length >= 12)
		{
			return password;
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PharmaBill.Drive." + storage.RootDirectory)));
	}
}
