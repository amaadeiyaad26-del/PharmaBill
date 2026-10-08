using System;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>
/// Licence status for Dashboard / Billing banners and the Upgrade / Activation flows.
/// </summary>
public sealed class LicenceViewModel : ObservableObject
{
	private readonly LicenseService _licenseService;
	private string _statusSummary = string.Empty;
	private string _machineId = string.Empty;
	private string _bannerText = string.Empty;
	private string _badgeText = "Subscribe / Activate";
	private bool _showExpiryWarning;
	private bool _showExpiredBanner;
	private bool _showClockTamper;
	private bool _showUpgradeButton = true;
	private bool _showLicensedBadge;
	private bool _hasFullAccess;

	public LicenceViewModel(LicenseService licenseService)
	{
		_licenseService = licenseService;
		Refresh();
	}

	public string StatusSummary
	{
		get => _statusSummary;
		private set => SetProperty(ref _statusSummary, value);
	}

	public string MachineId
	{
		get => _machineId;
		private set => SetProperty(ref _machineId, value);
	}

	public string BannerText
	{
		get => _bannerText;
		private set => SetProperty(ref _bannerText, value);
	}

	public string BadgeText
	{
		get => _badgeText;
		private set => SetProperty(ref _badgeText, value);
	}

	public bool ShowExpiryWarning
	{
		get => _showExpiryWarning;
		private set => SetProperty(ref _showExpiryWarning, value);
	}

	public bool ShowExpiredBanner
	{
		get => _showExpiredBanner;
		private set => SetProperty(ref _showExpiredBanner, value);
	}

	public bool ShowClockTamper
	{
		get => _showClockTamper;
		private set => SetProperty(ref _showClockTamper, value);
	}

	public bool ShowUpgradeButton
	{
		get => _showUpgradeButton;
		private set => SetProperty(ref _showUpgradeButton, value);
	}

	public bool ShowLicensedBadge
	{
		get => _showLicensedBadge;
		private set => SetProperty(ref _showLicensedBadge, value);
	}

	public bool HasFullAccess
	{
		get => _hasFullAccess;
		private set => SetProperty(ref _hasFullAccess, value);
	}

	public bool ShowAnyBanner => ShowExpiryWarning || ShowExpiredBanner || ShowClockTamper;

	private RelayCommand? _openRenewCommand;

	public IRelayCommand OpenRenewCommand => _openRenewCommand ??= new RelayCommand(OpenRenew);

	public void Refresh()
	{
		LicenceUiState state = _licenseService.GetUiState();
		StatusSummary = state.StatusSummary;
		MachineId = state.MachineId;
		BannerText = state.BannerText;
		ShowExpiryWarning = state.ShowExpiryWarning;
		ShowExpiredBanner = state.ShowExpiredBanner;
		ShowClockTamper = state.ShowClockTamper;
		HasFullAccess = state.HasFullAccess;

		if (state.IsActivated)
		{
			ShowUpgradeButton = state.ShowExpiryWarning || state.ShowClockTamper;
			ShowLicensedBadge = !ShowUpgradeButton;
			BadgeText = state.ValidUntilUtc is DateTime until
				? $"Licensed until {until:dd-MMM-yyyy}"
				: "Licensed";
		}
		else if (state.TrialDaysRemaining is > 0)
		{
			ShowUpgradeButton = true;
			ShowLicensedBadge = false;
			BadgeText = $"7-Day Free Trial ({state.TrialDaysRemaining.Value} days remaining)";
		}
		else
		{
			ShowUpgradeButton = true;
			ShowLicensedBadge = false;
			BadgeText = "Annual License Expired — Activate";
		}

		OnPropertyChanged(nameof(ShowAnyBanner));
	}

	private void OpenRenew()
	{
		Window? owner = Application.Current?.MainWindow;
		UpgradeWindow upgradeWindow = new UpgradeWindow();
		if (owner != null)
		{
			upgradeWindow.Owner = owner;
		}

		if (upgradeWindow.ShowDialog() == true)
		{
			_licenseService.InvalidateCache();
			Refresh();
		}
	}
}
