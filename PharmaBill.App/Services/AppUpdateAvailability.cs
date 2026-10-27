namespace PharmaBill.App.Services;

/// <param name="WasChecked">False when nothing could be asked (the Store edition run unpackaged): the button then opens the Store's page.</param>
public sealed record AppUpdateAvailability(bool IsUpdateAvailable, string CurrentVersion, string LatestVersion, bool IsCritical, UpdateChannel Channel, string? DownloadOrStoreUrl, string StatusMessage, string? DetailMessage = null, bool WasChecked = true);
