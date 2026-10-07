namespace PharmaBill.App.Services;

public sealed record AppUpdateAvailability(bool IsUpdateAvailable, string CurrentVersion, string LatestVersion, bool IsCritical, UpdateChannel Channel, string? DownloadOrStoreUrl, string StatusMessage, string? DetailMessage = null);
