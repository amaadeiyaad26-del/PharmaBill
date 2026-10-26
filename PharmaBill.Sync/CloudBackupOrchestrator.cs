
namespace PharmaBill.Sync;

public sealed class CloudBackupOrchestrator
{
	// Google Drive account backups were replaced by "Back up now" to a chosen folder (FolderBackupService):
	// a OneDrive, Google Drive or Dropbox folder gets the encrypted file off-site through its own sync app.
	// Nothing uploads behind the shop's back any more, including for PCs that linked an account earlier.
	public Task UploadToSelectedProvidersAsync(bool triggeredByBillSave, CancellationToken cancellationToken = default(CancellationToken), bool forceWhenConnected = false)
	{
		return Task.CompletedTask;
	}
}
