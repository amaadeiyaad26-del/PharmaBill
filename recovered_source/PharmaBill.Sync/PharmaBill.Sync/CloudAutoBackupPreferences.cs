namespace PharmaBill.Sync;

public sealed record CloudAutoBackupPreferences(CloudAutoBackupDestination Destinations)
{
	public static CloudAutoBackupPreferences Default { get; } = new CloudAutoBackupPreferences(CloudAutoBackupDestination.None);

	public bool IncludesGoogle => Destinations == CloudAutoBackupDestination.GoogleDrive;
}
