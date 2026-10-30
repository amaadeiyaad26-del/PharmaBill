using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;

namespace PharmaBill.App.ViewModels;

// The update indicator in both dashboards' headers: a quiet "Check for Updates" until a newer version is known, then
// "Update available". It only ever appears on the dashboards: nothing about updates pops up by itself or appears over
// the billing screen. Local Market edition: clicking it shows what the new version is (version, date, notes, size) with
// one button that opens the download page in the browser; the × hides it until a later version. Store edition: clicking
// it opens the Store's install flow.
public partial class MainWindowViewModel
{
	private const string CheckLabel = "Check for Updates";

	private bool _isUpdateAvailable;
	private bool _isCheckingForUpdates;
	private bool _isUpdateDetailsOpen;
	private PublishedRelease? _availableRelease;
	private string _updateButtonText = CheckLabel;
	private string _updateButtonToolTip = DefaultToolTip;
	private AsyncRelayCommand? checkForUpdatesCommand;
	private RelayCommand? dismissUpdateCommand;
	private RelayCommand? openUpdateDownloadCommand;
	private Action<string>? _openUri;

	private static string DefaultToolTip => AppEdition.Current == Edition.Store
		? "Ask the Microsoft Store whether a newer version of Pharma Remit is available"
		: "Ask pharmaremit.com whether a newer version of Pharma Remit is available";

	/// <summary>A newer version is waiting (and was not dismissed): the header indicator turns bright.</summary>
	public bool IsUpdateAvailable
	{
		get => _isUpdateAvailable;
		private set => SetProperty(ref _isUpdateAvailable, value);
	}

	public bool IsCheckingForUpdates
	{
		get => _isCheckingForUpdates;
		private set => SetProperty(ref _isCheckingForUpdates, value);
	}

	/// <summary>The details card under the indicator (Local Market edition). Closes when the user clicks elsewhere.</summary>
	public bool IsUpdateDetailsOpen
	{
		get => _isUpdateDetailsOpen;
		set => SetProperty(ref _isUpdateDetailsOpen, value);
	}

	/// <summary>The newer release the details card describes; null when there is none.</summary>
	public PublishedRelease? AvailableRelease
	{
		get => _availableRelease;
		private set
		{
			if (SetProperty(ref _availableRelease, value))
			{
				OnPropertyChanged(nameof(CanDismissUpdate));
			}
		}
	}

	/// <summary>The × beside the indicator: only for a website release (the Store reports no version to remember).</summary>
	public bool CanDismissUpdate => AvailableRelease != null && IsUpdateAvailable;

	public string UpdateButtonText
	{
		get => _updateButtonText;
		private set => SetProperty(ref _updateButtonText, value);
	}

	public string UpdateButtonToolTip
	{
		get => _updateButtonToolTip;
		private set => SetProperty(ref _updateButtonToolTip, value);
	}

	public IAsyncRelayCommand CheckForUpdatesCommand => checkForUpdatesCommand ??= new AsyncRelayCommand(CheckForUpdatesFromButtonAsync);

	public IRelayCommand DismissUpdateCommand => dismissUpdateCommand ??= new RelayCommand(DismissUpdate);

	public IRelayCommand OpenUpdateDownloadCommand => openUpdateDownloadCommand ??= new RelayCommand(OpenUpdateDownload);

	/// <summary>Opens a link (Store page or download page). Tests replace it so nothing opens during a run.</summary>
	internal Action<string> OpenUri
	{
		get => _openUri ?? OpenWithShell;
		set => _openUri = value;
	}

	private async Task CheckForUpdatesFromButtonAsync()
	{
		if (IsUpdateAvailable)
		{
			if (AvailableRelease != null)
			{
				IsUpdateDetailsOpen = true;
				return;
			}
			ShowItemQuickToast("Opening the Microsoft Store update…");
			await UpdateNowAsync();
			return;
		}
		IsCheckingForUpdates = true;
		UpdateButtonText = "Checking…";
		ShowItemQuickToast("Checking for updates…");
		try
		{
			await CheckForAppUpdatesAsync(force: true);
		}
		finally
		{
			IsCheckingForUpdates = false;
			if (!IsUpdateAvailable)
			{
				ShowUpdateButton(_appUpdates.LastResult);
			}
		}
		UpdateClickOutcome outcome = AfterCheck(AppEdition.Current, _appUpdates.LastResult);
		ShowItemQuickToast(outcome.Message);
		if (outcome.OpenUri != null)
		{
			OpenStorePage(outcome.OpenUri);
		}
		if (AvailableRelease != null && IsUpdateAvailable)
		{
			IsUpdateDetailsOpen = true;
		}
	}

	private void DismissUpdate()
	{
		if (AvailableRelease is PublishedRelease release)
		{
			_appUpdates.Dismiss(release.Version);
		}
		IsUpdateDetailsOpen = false;
		ShowUpdateButton(_appUpdates.LastResult);
	}

	// The one way to get the update: the download page, in the default browser. Nothing is downloaded or run here.
	private void OpenUpdateDownload()
	{
		if (AvailableRelease is not PublishedRelease release || !UpdateFeed.IsSafeDownloadUrl(release.DownloadUrl))
		{
			return;
		}
		IsUpdateDetailsOpen = false;
		try
		{
			OpenUri(release.DownloadUrl);
		}
		catch (Exception ex)
		{
			_confirmationService.NotifyError("Update", "Could not open the download page: " + PharmaBill.Core.UserMessage.Of(ex));
		}
	}

	/// <summary>What a click on "Check for Updates" tells the user, and which Store page it opens, once the check is done.</summary>
	internal readonly record struct UpdateClickOutcome(string Message, string? OpenUri);

	internal static UpdateClickOutcome AfterCheck(Edition edition, AppUpdateAvailability? result)
	{
		if (result is { IsUpdateAvailable: true })
		{
			return new UpdateClickOutcome(edition == Edition.Store
				? "A new version is available. Click Update Available to install it."
				: result.StatusMessage, null);
		}
		if (edition == Edition.Store)
		{
			// Not asked (run outside the Store package) or the Store did not answer: let the Store itself check.
			if (result == null || !result.WasChecked || result.Channel == UpdateChannel.None)
			{
				string page = string.IsNullOrWhiteSpace(result?.DownloadOrStoreUrl) ? AppEdition.StoreUpdatesUri : result!.DownloadOrStoreUrl!;
				return new UpdateClickOutcome("Opening the Microsoft Store to check for updates…", page);
			}
			return new UpdateClickOutcome($"Pharma Remit is up to date (version {result.CurrentVersion}).", null);
		}
		// The Local Market edition is not installed from the Store: never send it there (that would install a second copy).
		if (result == null || result.Channel == UpdateChannel.None)
		{
			return new UpdateClickOutcome(result?.StatusMessage ?? "Could not reach the update server. Pharma Remit works fully offline.", null);
		}
		return new UpdateClickOutcome($"You're on the latest version of Pharma Remit ({result.CurrentVersion}).", null);
	}

	private void OpenStorePage(string uri)
	{
		try
		{
			OpenUri(uri);
		}
		catch (Exception ex)
		{
			_confirmationService.NotifyError("Update", "Could not open the Microsoft Store: " + PharmaBill.Core.UserMessage.Of(ex));
		}
	}

	private static void OpenWithShell(string uri) =>
		System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });

	// Called with every update result (startup check, background checks, the button). A background check that could not
	// reach the server changes nothing on screen.
	private void ShowUpdateButton(AppUpdateAvailability? result)
	{
		bool available = result?.IsUpdateAvailable == true
			&& !(result.Release is PublishedRelease dismissed && _appUpdates.IsDismissed(dismissed.Version));
		IsUpdateAvailable = available;
		AvailableRelease = available ? result!.Release : null;
		OnPropertyChanged(nameof(CanDismissUpdate));
		if (!available)
		{
			IsUpdateDetailsOpen = false;
		}
		if (available)
		{
			UpdateButtonText = result!.Release is PublishedRelease release ? "Update available · " + release.Version : "Update Available";
			UpdateButtonToolTip = result.StatusMessage;
			return;
		}
		// "Up to date" only after a real answer; offline, with nothing to ask, or with a newer version the user hid, it
		// stays an invitation to check.
		UpdateButtonText = result is { WasChecked: true, IsUpdateAvailable: false } && result.Channel != UpdateChannel.None
			? "Up to date ✓"
			: CheckLabel;
		UpdateButtonToolTip = result?.StatusMessage ?? DefaultToolTip;
	}
}
