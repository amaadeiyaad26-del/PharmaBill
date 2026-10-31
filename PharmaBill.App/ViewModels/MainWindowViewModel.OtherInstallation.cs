using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;

namespace PharmaBill.App.ViewModels;

// The other edition of Pharma Remit is installed on this computer too: a warning on the dashboards (never on a billing
// screen), naming both data folders, until the pharmacist dismisses it. Not a dialog: nothing is blocked.
public partial class MainWindowViewModel
{
	private string _otherInstallationWarning = string.Empty;
	private OneTimeNoticeStore? _noticeStore;
	private RelayCommand? dismissOtherInstallationCommand;

	public string OtherInstallationWarning
	{
		get => _otherInstallationWarning;
		private set
		{
			if (SetProperty(ref _otherInstallationWarning, value))
			{
				OnPropertyChanged(nameof(ShowOtherInstallationWarning));
			}
		}
	}

	public bool ShowOtherInstallationWarning => OtherInstallationWarning.Length > 0;

	public IRelayCommand DismissOtherInstallationCommand => dismissOtherInstallationCommand ??= new RelayCommand(() =>
	{
		_noticeStore?.Dismiss(OtherInstallationNotice.DismissKey);
		OtherInstallationWarning = string.Empty;
	});

	/// <summary>Shows the warning unless it was dismissed before. Null <paramref name="warning"/>: the other edition is absent.</summary>
	internal void ApplyOtherInstallationWarning(string? warning, OneTimeNoticeStore store)
	{
		_noticeStore = store;
		OtherInstallationWarning = warning != null && !store.IsDismissed(OtherInstallationNotice.DismissKey) ? warning : string.Empty;
	}
}
