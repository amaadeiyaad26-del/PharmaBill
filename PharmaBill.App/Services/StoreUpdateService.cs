using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Windows.Services.Store;

namespace PharmaBill.App.Services;

/// <summary>
/// Store edition only (not compiled into the Local Market build): asks the Microsoft Store whether this install has a
/// newer package, and hands installing to the Store. The Store compares versions itself.
/// </summary>
public sealed class StoreUpdateService : IAppUpdateService, IDisposable
{
	private readonly AppVersionInfo _versionInfo;

	private readonly UpdateCheckPreferencesStore _preferences;

	private readonly ILogger<StoreUpdateService> _logger;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	private StoreContext? _storeContext;

	private IReadOnlyList<StorePackageUpdate>? _pendingStoreUpdates;

	public AppUpdateAvailability? LastResult { get; private set; }

	public event EventHandler<AppUpdateAvailability>? UpdateStateChanged;

	public StoreUpdateService(AppVersionInfo versionInfo, UpdateCheckPreferencesStore preferences, ILogger<StoreUpdateService> logger)
	{
		_versionInfo = versionInfo;
		_preferences = preferences;
		_logger = logger;
	}

	public void Dismiss(string version) => _preferences.Save(_preferences.Load() with { DismissedVersion = version });

	// The Store reports no version number, only that a newer package exists.
	public bool IsDismissed(string version) => false;

	public async Task<AppUpdateAvailability> CheckForUpdatesAsync(bool force = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			UpdateCheckPreferences prefs = _preferences.Load();
			if (!force && prefs.LastCheckedUtc is DateTimeOffset last && DateTimeOffset.UtcNow - last < TimeSpan.FromHours(24) && LastResult != null)
			{
				return LastResult;
			}
			AppUpdateAvailability result;
			try
			{
				result = await CheckStoreAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.LogDebug(ex, "Store update check failed; app continues offline.");
				result = new AppUpdateAvailability(IsUpdateAvailable: false, _versionInfo.Version, _versionInfo.Version, IsCritical: false, UpdateChannel.None, null,
					"Could not reach the Microsoft Store to check for updates. You can keep using Pharma Remit.", PharmaBill.Core.UserMessage.Of(ex));
			}
			_preferences.Save(prefs with { LastCheckedUtc = DateTimeOffset.UtcNow });
			LastResult = result;
			UpdateStateChanged?.Invoke(this, result);
			return result;
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task LaunchUpdateAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		// The Store shows its install dialog over this window; take its handle while still on the UI thread.
		IntPtr owner = MainWindowHandle();
		AppUpdateAvailability result = LastResult ?? await CheckForUpdatesAsync(force: true, cancellationToken).ConfigureAwait(false);
		if (!result.IsUpdateAvailable)
		{
			return;
		}
		IReadOnlyList<StorePackageUpdate>? pending = _pendingStoreUpdates;
		if (pending is { Count: > 0 } && _storeContext != null)
		{
			try
			{
				// A desktop app must give the Store its window before the Store can show its dialog.
				if (owner != IntPtr.Zero)
				{
					WinRT.Interop.InitializeWithWindow.Initialize(_storeContext, owner);
				}
				StorePackageUpdateResult installed = await _storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(pending).AsTask(cancellationToken).ConfigureAwait(false);
				_logger.LogInformation("Store update request finished: {State}", installed.OverallState);
				if (installed.OverallState is StorePackageUpdateState.Completed or StorePackageUpdateState.Canceled)
				{
					return;
				}
			}
			catch (Exception exception)
			{
				_logger.LogInformation(exception, "Store install request failed; opening the Store page.");
			}
		}
		string page = string.IsNullOrWhiteSpace(result.DownloadOrStoreUrl) ? AppEdition.StoreUpdatesUri : result.DownloadOrStoreUrl;
		Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
	}

	/// <summary>
	/// Asks the Microsoft Store whether this install has a newer package. Run unpackaged (a developer build) there is
	/// no Store install to ask, and the button opens the Store's Downloads and updates page.
	/// </summary>
	private async Task<AppUpdateAvailability> CheckStoreAsync(CancellationToken cancellationToken)
	{
		_pendingStoreUpdates = null;
		if (!IsStorePackaged())
		{
			return AppUpdateAvailability.StoreHandlesUpdates(_versionInfo.Version);
		}
		_storeContext ??= StoreContext.GetDefault();
		IReadOnlyList<StorePackageUpdate> updates = await _storeContext.GetAppAndOptionalStorePackageUpdatesAsync().AsTask(cancellationToken).ConfigureAwait(false);
		_pendingStoreUpdates = updates;
		string storePage = await StoreProductPageUriAsync(cancellationToken).ConfigureAwait(false);
		if (updates.Count == 0)
		{
			return new AppUpdateAvailability(IsUpdateAvailable: false, _versionInfo.Version, _versionInfo.Version, IsCritical: false, UpdateChannel.MicrosoftStore, storePage, "Up to date ✓ (v" + _versionInfo.Display + ")");
		}
		bool mandatory = updates.Any(update => update.Mandatory);
		return new AppUpdateAvailability(IsUpdateAvailable: true, _versionInfo.Version, _versionInfo.Version, IsCritical: false, UpdateChannel.MicrosoftStore, storePage,
			"A new version of Pharma Remit is available in the Microsoft Store" + (mandatory ? " (required by the publisher)." : ".") + " Click Update Available to install it.");
	}

	private static bool IsStorePackaged()
	{
		try
		{
			return Windows.ApplicationModel.Package.Current != null;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
	}

	private async Task<string> StoreProductPageUriAsync(CancellationToken cancellationToken)
	{
		try
		{
			string? storeId = _storeContext == null ? null
				: (await _storeContext.GetStoreProductForCurrentAppAsync().AsTask(cancellationToken).ConfigureAwait(false))?.Product?.StoreId;
			return string.IsNullOrWhiteSpace(storeId) ? AppEdition.StoreUpdatesUri : "ms-windows-store://pdp/?ProductId=" + storeId;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Store product id not available; using the Downloads and updates page.");
			return AppEdition.StoreUpdatesUri;
		}
	}

	private static IntPtr MainWindowHandle()
	{
		try
		{
			System.Windows.Window? window = System.Windows.Application.Current?.MainWindow;
			return window == null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle;
		}
		catch (InvalidOperationException)
		{
			return IntPtr.Zero;
		}
	}

	public void Dispose() => _gate.Dispose();
}
