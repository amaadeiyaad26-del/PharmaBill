using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;

namespace PharmaBill.App.ViewModels;

// The update button in both dashboards' headers: quiet ("Check for Updates") until the Microsoft Store (or, for the
// Local Market edition, the release feed) has a newer version, then bright ("Update Available"). Clicking it checks
// now, or, when an update is waiting, opens the Store's install flow. Every click says what happened in the toast at
// the top of the page, so a check that finds nothing never looks like a dead button.
public partial class MainWindowViewModel
{
	private const string CheckLabel = "Check for Updates";

	private bool _isUpdateAvailable;
	private bool _isCheckingForUpdates;
	private string _updateButtonText = CheckLabel;
	private string _updateButtonToolTip = "Ask the Microsoft Store whether a newer version of Pharma Remit is available";
	private AsyncRelayCommand? checkForUpdatesCommand;
	private Action<string>? _openUri;

	/// <summary>A newer version is waiting: the header button turns bright.</summary>
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

	/// <summary>Opens a Microsoft Store link. Tests replace it so no Store window opens during a run.</summary>
	internal Action<string> OpenUri
	{
		get => _openUri ?? OpenWithShell;
		set => _openUri = value;
	}

	private async Task CheckForUpdatesFromButtonAsync()
	{
		if (IsUpdateAvailable)
		{
			ShowItemQuickToast(AppEdition.Current == Edition.Store ? "Opening the Microsoft Store update…" : "Starting the update…");
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
	}

	/// <summary>What a click on "Check for Updates" tells the user, and which Store page it opens, once the check is done.</summary>
	internal readonly record struct UpdateClickOutcome(string Message, string? OpenUri);

	internal static UpdateClickOutcome AfterCheck(Edition edition, AppUpdateAvailability? result)
	{
		if (result is { IsUpdateAvailable: true })
		{
			return new UpdateClickOutcome("A new version is available. Click Update Available to install it.", null);
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
			return new UpdateClickOutcome(result?.StatusMessage ?? "Could not check for updates. Pharma Remit stays fully usable offline.", null);
		}
		return new UpdateClickOutcome($"Pharma Remit is up to date (version {result.CurrentVersion}).", null);
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

	// Called with every update result (startup check, background checks, the button).
	private void ShowUpdateButton(AppUpdateAvailability? result)
	{
		IsUpdateAvailable = result?.IsUpdateAvailable == true;
		if (IsUpdateAvailable)
		{
			UpdateButtonText = "Update Available";
			UpdateButtonToolTip = result!.StatusMessage;
			return;
		}
		// "Up to date" only after a real answer; offline, or with nothing to ask, it stays an invitation to check.
		UpdateButtonText = result is { WasChecked: true } && result.Channel != UpdateChannel.None
			? "Up to date ✓"
			: CheckLabel;
		UpdateButtonToolTip = result?.StatusMessage ?? "Ask the Microsoft Store whether a newer version of Pharma Remit is available";
	}
}
