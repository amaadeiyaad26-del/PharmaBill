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
	// Google Drive account backups were replaced by "Back up now" to a chosen folder (FolderBackupService):
	// a OneDrive, Google Drive or Dropbox folder gets the encrypted file off-site through its own sync app.
	// Nothing uploads behind the shop's back any more, including for PCs that linked an account earlier.
	public Task UploadToSelectedProvidersAsync(bool triggeredByBillSave, CancellationToken cancellationToken = default(CancellationToken), bool forceWhenConnected = false)
	{
		return Task.CompletedTask;
	}
}
