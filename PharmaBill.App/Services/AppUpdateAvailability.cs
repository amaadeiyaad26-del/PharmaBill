namespace PharmaBill.App.Services;

/// <param name="WasChecked">False when nothing could be asked (the Store edition run unpackaged): the button then opens the Store's page.</param>
/// <param name="Release">The website's published release when it is newer than this copy (Local Market edition).</param>
public sealed record AppUpdateAvailability(bool IsUpdateAvailable, string CurrentVersion, string LatestVersion, bool IsCritical, UpdateChannel Channel, string? DownloadOrStoreUrl, string StatusMessage, string? DetailMessage = null, bool WasChecked = true, PublishedRelease? Release = null)
{
	/// <summary>
	/// Store edition: the Store installs updates, so nothing points to the downloadable installer. The button opens the
	/// Store's Downloads and updates page instead.
	/// </summary>
	public static AppUpdateAvailability StoreHandlesUpdates(string currentVersion) =>
		new(IsUpdateAvailable: false, currentVersion, currentVersion, IsCritical: false, UpdateChannel.MicrosoftStore, AppEdition.StoreUpdatesUri,
			"Updates for Pharma Remit are installed by the Microsoft Store. Open the Store's Downloads and updates page to update now.", WasChecked: false);
}
