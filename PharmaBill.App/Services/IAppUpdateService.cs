namespace PharmaBill.App.Services;

public interface IAppUpdateService
{
	AppUpdateAvailability? LastResult { get; }

	event EventHandler<AppUpdateAvailability>? UpdateStateChanged;

	/// <summary>Asks whether a newer version exists: at most once a day, unless <paramref name="force"/> (the user asked).</summary>
	Task<AppUpdateAvailability> CheckForUpdatesAsync(bool force = false, CancellationToken cancellationToken = default(CancellationToken));

	/// <summary>Store edition: the Store's install flow. Local Market edition: opens the download page in the browser.</summary>
	Task LaunchUpdateAsync(CancellationToken cancellationToken = default(CancellationToken));

	/// <summary>Hides the "update available" indicator for this version.</summary>
	void Dismiss(string version);

	bool IsDismissed(string version);
}
