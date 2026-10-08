using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.App.Services;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync;

namespace PharmaBill.App.ViewModels;

public class MainWindowViewModel : ObservableObject
{
	private readonly INavigationService _navigationService;

	private readonly ILanguageService _languageService;

	private readonly SettingsPageViewModel _settingsPage;

	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _currentSession;

	private readonly ILogger<MainWindowViewModel> _logger;

	private readonly SyncFolderExchangeService _folderSync;

	private readonly CatalogSearchViewModel _catalogSearch;

	private readonly StockPageViewModel _stockPage;

	private readonly StockTransferPageViewModel _stockTransferPage;

	private readonly PurchasePageViewModel _purchasePage;

	private readonly WholesaleCustomerPageViewModel _wholesaleCustomerPage;

	private readonly WholesaleBillingPageViewModel _wholesaleBillingPage;

	private readonly WholesalePricingPageViewModel _wholesalePricingPage;

	private readonly WholesaleAccountsPageViewModel _wholesaleAccountsPage;

	private readonly WholesaleReconciliationPageViewModel _wholesaleReconciliationPage;

	private readonly DunningDashboardPageViewModel _dunningDashboardPage;

	private readonly WholesaleReturnsPageViewModel _wholesaleReturnsPage;

	private readonly StockInHandPageViewModel _stockInHandPage;

	private readonly WholesaleReportsPageViewModel _wholesaleReportsPage;

	private readonly ReportsPageViewModel _reportsPage;

	private readonly GstReturnsPageViewModel _gstReturnsPage;

	private readonly DashboardPageViewModel _dashboardPage;

	private readonly BackupPageViewModel _backupPage;

	private readonly SyncSettingsPageViewModel _syncPage;

	private readonly RetailBillingViewModel _retailBillingPage;

	private readonly IFilePickerService _filePickerService;

	private readonly TabularExportService _tabularExportService;

	private readonly DrugRecordsService _drugRecordsService;

	private readonly StatutoryRegisterService _statutoryRegisterService;

	private readonly SensitiveAccessService _sensitiveAccessService;

	private readonly IConfirmationService _confirmationService;

	private readonly IAccountDialogService _accountDialogs;

	private readonly IUserManualDialogService _userManualDialogs;

	private readonly CatalogueService _catalogueService;

	private readonly GoogleDriveSyncService _googleDrive;

	private readonly CloudBackupOrchestrator _cloudBackup;

	private readonly ZeroConfigSyncCoordinator _zeroConfigSync;

	private readonly UsbBackupSettingsStore _usbBackupSettings;

	private readonly ActiveBillingDeskModeStore _activeBillingDeskModeStore;

	private readonly IAppUpdateService _appUpdates;

	private readonly DispatcherTimer _clockTimer;

	private readonly DispatcherTimer _updatePollTimer;

	private BusinessMode _businessMode;

	private ActiveBillingDeskMode _activeBillingDeskMode;

	private bool _criticalUpdateDialogShown;

	private DateTime _shiftStartedLocal = DateTime.Now;

	private CancellationTokenSource? _welcomeDismissCts;

	private object _currentPage;

	private FlowDirection _flowDirection;

	private string _localDateTime = string.Empty;

	private string _pharmacyName = "Your Pharmacy";

	private string _loggedInUser = string.Empty;

	private string _userRoleLabel = string.Empty;

	private string _activeBranchCode = "BR01-MAIN";

	private string _shiftStartedDisplay = string.Empty;

	private string _profileStatusSummary = string.Empty;

	private bool _showWelcomeToast;

	private string _welcomeHeadline = string.Empty;

	private string _welcomeSubtitle = string.Empty;

	private string _welcomeStatusLine = string.Empty;

	private bool _isProfileStatusPopupOpen;

	private string _modeStatus = string.Empty;

	private string _modeBadgeKind = "Combined";

	private string _trialStatus = string.Empty;

	private string _trialStatusToolTip = "Licence status";

	private bool _isReadOnly;

	private bool _canUseRetailBilling = true;

	private bool _canUseWholesaleBilling;

	private string _folderSyncWarning = string.Empty;

	private bool _showUsbBackupReminder;

	private bool _showUpdateBanner;

	private bool _isCriticalUpdateRequired;

	private string _updateBannerText = string.Empty;

	private string _barcodeToastMessage = string.Empty;

	private RetailStockChoice? _scannedItemPopover;

	private string _assistantQuery = string.Empty;

	private string _assistantMessage = string.Empty;

	private bool _isBusy;

	private string _busyMessage = "Loading...";

	private bool _isImportingCatalogue;

	private string _catalogueImportStatus = "Indexing 253,973 medicines in background...";

	private double _catalogueImportProgress;

	private bool _isDriveSyncing;

	private string _driveSyncStatus = string.Empty;

	private double _driveSyncProgress;

	private bool _isHeaderSearchFlyoutOpen;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? usbBackupReminderBackupNowCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? usbBackupReminderSnoozeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? dismissWelcomeToastCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openProfileStatusPopupCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? closeProfileStatusPopupCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? askAssistantCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? closeHeaderSearchFlyoutCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<MedicineSearchResult?>? openHeaderResultInStockCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<MedicineSearchResult?>? openHeaderResultInBillingCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openLicenceCommand;

	private RelayCommand? openRenewLicenseCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openProfileCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? dismissBarcodeToastCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? dismissScannedItemPopoverCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addScannedItemToBillCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? navigateCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? updateNowCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? remindUpdateTomorrowCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? selectRetailDeskModeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? selectWholesaleDeskModeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? selectCombinedDeskModeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openUserManualCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? shortcutNewBillCommand;

	private AsyncRelayCommand? shortcutSaveAndPrintCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? shortcutSearchCommand;

	private RelayCommand? shortcutFocusCustomerCommand;

	private RelayCommand? shortcutFocusMedicineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? focusSettingsSearchCommand;

	public bool ShowLicenceStatusPill
	{
		get
		{
			if (!IsReadOnly && !string.Equals(TrialStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase))
			{
				return string.Equals(TrialStatus, "GRACE OFFLINE", StringComparison.OrdinalIgnoreCase);
			}
			return true;
		}
	}

	public bool CanUseCombinedDeskMode => _businessMode == BusinessMode.Both;

	public bool IsRetailDeskActive => _activeBillingDeskMode == ActiveBillingDeskMode.Retail;

	public bool IsWholesaleDeskActive => _activeBillingDeskMode == ActiveBillingDeskMode.Wholesale;

	public bool IsCombinedDeskActive => _activeBillingDeskMode == ActiveBillingDeskMode.Combined;

	public bool CanRemindUpdateTomorrow => !IsCriticalUpdateRequired;

	public bool HasBarcodeToast => !string.IsNullOrWhiteSpace(BarcodeToastMessage);

	public bool HasScannedItemPopover => (object)ScannedItemPopover != null;

	public bool HasFolderSyncWarning => !string.IsNullOrWhiteSpace(FolderSyncWarning);

	public string ReadOnlyBanner
	{
		get
		{
			if (LicenseManager.IsClockTampered())
			{
				return "READ-ONLY MODE — " + LicenseManager.ClockTamperMessage + " Drug Records, past invoices, stock, GST and reports stay available.";
			}

			if (LicenseManager.IsHardwareMismatch())
			{
				return "READ-ONLY MODE — hardware fingerprint mismatch (anti-cloning). This data/license is bound to another PC. Records stay readable.";
			}

			if (!LicenseManager.HasFullAccess())
			{
				return "Annual License Expired. Enter renewal key to continue billing. Drug Records, past invoices, stock in hand, GST summaries and reports remain readable and exportable.";
			}

			return "READ-ONLY MODE — a required drug licence for the active mode is missing or expired. Viewing, searching, printing, exporting and backup are still available.";
		}
	}

	public bool ShowAnnualLicenseWarning =>
		LicenseManager.IsWithinExpiryWarningWindow() || LicenseManager.IsClockTampered();

	public string AnnualLicenseWarningText =>
		LicenseManager.GetExpiryWarningMessage()
		?? "Your annual license is approaching expiry. Contact support to renew.";

	public ObservableCollection<NavigationItem> NavigationItems { get; } = new ObservableCollection<NavigationItem>();

	public string CurrentSectionKey => _navigationService.CurrentSectionKey;

	public CatalogSearchViewModel CatalogSearch => _catalogSearch;

	public bool IsDebugBuild => false;

	public bool HasStatusBanner => IsImportingCatalogue;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public object CurrentPage
	{
		get
		{
			return _currentPage;
		}
		[MemberNotNull("_currentPage")]
		set
		{
			if (!EqualityComparer<object>.Default.Equals(_currentPage, value))
			{
				OnPropertyChanging(nameof(CurrentPage));
				_currentPage = value;
				OnPropertyChanged(nameof(CurrentPage));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public FlowDirection FlowDirection
	{
		get
		{
			return _flowDirection;
		}
		set
		{
			if (!EqualityComparer<FlowDirection>.Default.Equals(_flowDirection, value))
			{
				OnPropertyChanging(nameof(FlowDirection));
				_flowDirection = value;
				OnPropertyChanged(nameof(FlowDirection));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LocalDateTime
	{
		get
		{
			return _localDateTime;
		}
		[MemberNotNull("_localDateTime")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_localDateTime, value))
			{
				OnPropertyChanging(nameof(LocalDateTime));
				_localDateTime = value;
				OnPropertyChanged(nameof(LocalDateTime));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PharmacyName
	{
		get
		{
			return _pharmacyName;
		}
		[MemberNotNull("_pharmacyName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pharmacyName, value))
			{
				OnPropertyChanging(nameof(PharmacyName));
				_pharmacyName = value;
				OnPropertyChanged(nameof(PharmacyName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LoggedInUser
	{
		get
		{
			return _loggedInUser;
		}
		[MemberNotNull("_loggedInUser")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_loggedInUser, value))
			{
				OnPropertyChanging(nameof(LoggedInUser));
				_loggedInUser = value;
				OnPropertyChanged(nameof(LoggedInUser));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UserRoleLabel
	{
		get
		{
			return _userRoleLabel;
		}
		[MemberNotNull("_userRoleLabel")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_userRoleLabel, value))
			{
				OnPropertyChanging(nameof(UserRoleLabel));
				_userRoleLabel = value;
				OnPropertyChanged(nameof(UserRoleLabel));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ActiveBranchCode
	{
		get
		{
			return _activeBranchCode;
		}
		[MemberNotNull("_activeBranchCode")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_activeBranchCode, value))
			{
				OnPropertyChanging(nameof(ActiveBranchCode));
				_activeBranchCode = value;
				OnPropertyChanged(nameof(ActiveBranchCode));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ShiftStartedDisplay
	{
		get
		{
			return _shiftStartedDisplay;
		}
		[MemberNotNull("_shiftStartedDisplay")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_shiftStartedDisplay, value))
			{
				OnPropertyChanging(nameof(ShiftStartedDisplay));
				_shiftStartedDisplay = value;
				OnPropertyChanged(nameof(ShiftStartedDisplay));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ProfileStatusSummary
	{
		get
		{
			return _profileStatusSummary;
		}
		[MemberNotNull("_profileStatusSummary")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_profileStatusSummary, value))
			{
				OnPropertyChanging(nameof(ProfileStatusSummary));
				_profileStatusSummary = value;
				OnPropertyChanged(nameof(ProfileStatusSummary));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowWelcomeToast
	{
		get
		{
			return _showWelcomeToast;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showWelcomeToast, value))
			{
				OnPropertyChanging(nameof(ShowWelcomeToast));
				_showWelcomeToast = value;
				OnPropertyChanged(nameof(ShowWelcomeToast));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string WelcomeHeadline
	{
		get
		{
			return _welcomeHeadline;
		}
		[MemberNotNull("_welcomeHeadline")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_welcomeHeadline, value))
			{
				OnPropertyChanging(nameof(WelcomeHeadline));
				_welcomeHeadline = value;
				OnPropertyChanged(nameof(WelcomeHeadline));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string WelcomeSubtitle
	{
		get
		{
			return _welcomeSubtitle;
		}
		[MemberNotNull("_welcomeSubtitle")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_welcomeSubtitle, value))
			{
				OnPropertyChanging(nameof(WelcomeSubtitle));
				_welcomeSubtitle = value;
				OnPropertyChanged(nameof(WelcomeSubtitle));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string WelcomeStatusLine
	{
		get
		{
			return _welcomeStatusLine;
		}
		[MemberNotNull("_welcomeStatusLine")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_welcomeStatusLine, value))
			{
				OnPropertyChanging(nameof(WelcomeStatusLine));
				_welcomeStatusLine = value;
				OnPropertyChanged(nameof(WelcomeStatusLine));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsProfileStatusPopupOpen
	{
		get
		{
			return _isProfileStatusPopupOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isProfileStatusPopupOpen, value))
			{
				OnPropertyChanging(nameof(IsProfileStatusPopupOpen));
				_isProfileStatusPopupOpen = value;
				OnPropertyChanged(nameof(IsProfileStatusPopupOpen));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ModeStatus
	{
		get
		{
			return _modeStatus;
		}
		[MemberNotNull("_modeStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_modeStatus, value))
			{
				OnPropertyChanging(nameof(ModeStatus));
				_modeStatus = value;
				OnPropertyChanged(nameof(ModeStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ModeBadgeKind
	{
		get
		{
			return _modeBadgeKind;
		}
		[MemberNotNull("_modeBadgeKind")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_modeBadgeKind, value))
			{
				OnPropertyChanging(nameof(ModeBadgeKind));
				_modeBadgeKind = value;
				OnPropertyChanged(nameof(ModeBadgeKind));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string TrialStatus
	{
		get
		{
			return _trialStatus;
		}
		[MemberNotNull("_trialStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_trialStatus, value))
			{
				OnPropertyChanging(nameof(TrialStatus));
				OnPropertyChanging(nameof(ShowLicenceStatusPill));
				_trialStatus = value;
				OnPropertyChanged(nameof(TrialStatus));
				OnPropertyChanged(nameof(ShowLicenceStatusPill));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string TrialStatusToolTip
	{
		get
		{
			return _trialStatusToolTip;
		}
		[MemberNotNull("_trialStatusToolTip")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_trialStatusToolTip, value))
			{
				OnPropertyChanging(nameof(TrialStatusToolTip));
				_trialStatusToolTip = value;
				OnPropertyChanged(nameof(TrialStatusToolTip));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsReadOnly
	{
		get
		{
			return _isReadOnly;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isReadOnly, value))
			{
				OnPropertyChanging(nameof(IsReadOnly));
				OnPropertyChanging(nameof(ShowLicenceStatusPill));
				_isReadOnly = value;
				OnPropertyChanged(nameof(IsReadOnly));
				OnPropertyChanged(nameof(ShowLicenceStatusPill));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CanUseRetailBilling
	{
		get
		{
			return _canUseRetailBilling;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_canUseRetailBilling, value))
			{
				OnPropertyChanging(nameof(CanUseRetailBilling));
				_canUseRetailBilling = value;
				OnPropertyChanged(nameof(CanUseRetailBilling));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CanUseWholesaleBilling
	{
		get
		{
			return _canUseWholesaleBilling;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_canUseWholesaleBilling, value))
			{
				OnPropertyChanging(nameof(CanUseWholesaleBilling));
				_canUseWholesaleBilling = value;
				OnPropertyChanged(nameof(CanUseWholesaleBilling));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FolderSyncWarning
	{
		get
		{
			return _folderSyncWarning;
		}
		[MemberNotNull("_folderSyncWarning")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_folderSyncWarning, value))
			{
				OnPropertyChanging(nameof(FolderSyncWarning));
				_folderSyncWarning = value;
				OnFolderSyncWarningChanged(value);
				OnPropertyChanged(nameof(FolderSyncWarning));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowUsbBackupReminder
	{
		get
		{
			return _showUsbBackupReminder;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showUsbBackupReminder, value))
			{
				OnPropertyChanging(nameof(ShowUsbBackupReminder));
				_showUsbBackupReminder = value;
				OnPropertyChanged(nameof(ShowUsbBackupReminder));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowUpdateBanner
	{
		get
		{
			return _showUpdateBanner;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showUpdateBanner, value))
			{
				OnPropertyChanging(nameof(ShowUpdateBanner));
				_showUpdateBanner = value;
				OnPropertyChanged(nameof(ShowUpdateBanner));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsCriticalUpdateRequired
	{
		get
		{
			return _isCriticalUpdateRequired;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isCriticalUpdateRequired, value))
			{
				OnPropertyChanging(nameof(IsCriticalUpdateRequired));
				OnPropertyChanging(nameof(CanRemindUpdateTomorrow));
				_isCriticalUpdateRequired = value;
				OnPropertyChanged(nameof(IsCriticalUpdateRequired));
				OnPropertyChanged(nameof(CanRemindUpdateTomorrow));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpdateBannerText
	{
		get
		{
			return _updateBannerText;
		}
		[MemberNotNull("_updateBannerText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_updateBannerText, value))
			{
				OnPropertyChanging(nameof(UpdateBannerText));
				_updateBannerText = value;
				OnPropertyChanged(nameof(UpdateBannerText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BarcodeToastMessage
	{
		get
		{
			return _barcodeToastMessage;
		}
		[MemberNotNull("_barcodeToastMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_barcodeToastMessage, value))
			{
				OnPropertyChanging(nameof(BarcodeToastMessage));
				OnPropertyChanging(nameof(HasBarcodeToast));
				_barcodeToastMessage = value;
				OnPropertyChanged(nameof(BarcodeToastMessage));
				OnPropertyChanged(nameof(HasBarcodeToast));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RetailStockChoice? ScannedItemPopover
	{
		get
		{
			return _scannedItemPopover;
		}
		set
		{
			if (!EqualityComparer<RetailStockChoice>.Default.Equals(_scannedItemPopover, value))
			{
				OnPropertyChanging(nameof(ScannedItemPopover));
				OnPropertyChanging(nameof(HasScannedItemPopover));
				_scannedItemPopover = value;
				OnPropertyChanged(nameof(ScannedItemPopover));
				OnPropertyChanged(nameof(HasScannedItemPopover));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AssistantQuery
	{
		get
		{
			return _assistantQuery;
		}
		[MemberNotNull("_assistantQuery")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_assistantQuery, value))
			{
				OnPropertyChanging(nameof(AssistantQuery));
				_assistantQuery = value;
				OnPropertyChanged(nameof(AssistantQuery));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AssistantMessage
	{
		get
		{
			return _assistantMessage;
		}
		[MemberNotNull("_assistantMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_assistantMessage, value))
			{
				OnPropertyChanging(nameof(AssistantMessage));
				_assistantMessage = value;
				OnPropertyChanged(nameof(AssistantMessage));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsBusy
	{
		get
		{
			return _isBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isBusy, value))
			{
				OnPropertyChanging(nameof(IsBusy));
				_isBusy = value;
				OnPropertyChanged(nameof(IsBusy));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BusyMessage
	{
		get
		{
			return _busyMessage;
		}
		[MemberNotNull("_busyMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_busyMessage, value))
			{
				OnPropertyChanging(nameof(BusyMessage));
				_busyMessage = value;
				OnPropertyChanged(nameof(BusyMessage));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsImportingCatalogue
	{
		get
		{
			return _isImportingCatalogue;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isImportingCatalogue, value))
			{
				OnPropertyChanging(nameof(IsImportingCatalogue));
				_isImportingCatalogue = value;
				OnIsImportingCatalogueChanged(value);
				OnPropertyChanged(nameof(IsImportingCatalogue));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CatalogueImportStatus
	{
		get
		{
			return _catalogueImportStatus;
		}
		[MemberNotNull("_catalogueImportStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_catalogueImportStatus, value))
			{
				OnPropertyChanging(nameof(CatalogueImportStatus));
				_catalogueImportStatus = value;
				OnPropertyChanged(nameof(CatalogueImportStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double CatalogueImportProgress
	{
		get
		{
			return _catalogueImportProgress;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(_catalogueImportProgress, value))
			{
				OnPropertyChanging(nameof(CatalogueImportProgress));
				_catalogueImportProgress = value;
				OnPropertyChanged(nameof(CatalogueImportProgress));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsDriveSyncing
	{
		get
		{
			return _isDriveSyncing;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isDriveSyncing, value))
			{
				OnPropertyChanging(nameof(IsDriveSyncing));
				_isDriveSyncing = value;
				OnPropertyChanged(nameof(IsDriveSyncing));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DriveSyncStatus
	{
		get
		{
			return _driveSyncStatus;
		}
		[MemberNotNull("_driveSyncStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_driveSyncStatus, value))
			{
				OnPropertyChanging(nameof(DriveSyncStatus));
				_driveSyncStatus = value;
				OnPropertyChanged(nameof(DriveSyncStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public double DriveSyncProgress
	{
		get
		{
			return _driveSyncProgress;
		}
		set
		{
			if (!EqualityComparer<double>.Default.Equals(_driveSyncProgress, value))
			{
				OnPropertyChanging(nameof(DriveSyncProgress));
				_driveSyncProgress = value;
				OnPropertyChanged(nameof(DriveSyncProgress));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsHeaderSearchFlyoutOpen
	{
		get
		{
			return _isHeaderSearchFlyoutOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isHeaderSearchFlyoutOpen, value))
			{
				OnPropertyChanging(nameof(IsHeaderSearchFlyoutOpen));
				_isHeaderSearchFlyoutOpen = value;
				OnPropertyChanged(nameof(IsHeaderSearchFlyoutOpen));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand UsbBackupReminderBackupNowCommand => usbBackupReminderBackupNowCommand ?? (usbBackupReminderBackupNowCommand = new RelayCommand(UsbBackupReminderBackupNow));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand UsbBackupReminderSnoozeCommand => usbBackupReminderSnoozeCommand ?? (usbBackupReminderSnoozeCommand = new RelayCommand(UsbBackupReminderSnooze));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand DismissWelcomeToastCommand => dismissWelcomeToastCommand ?? (dismissWelcomeToastCommand = new RelayCommand(DismissWelcomeToast));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenProfileStatusPopupCommand => openProfileStatusPopupCommand ?? (openProfileStatusPopupCommand = new RelayCommand(OpenProfileStatusPopup));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CloseProfileStatusPopupCommand => closeProfileStatusPopupCommand ?? (closeProfileStatusPopupCommand = new RelayCommand(CloseProfileStatusPopup));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AskAssistantCommand => askAssistantCommand ?? (askAssistantCommand = new AsyncRelayCommand(AskAssistantAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CloseHeaderSearchFlyoutCommand => closeHeaderSearchFlyoutCommand ?? (closeHeaderSearchFlyoutCommand = new RelayCommand(CloseHeaderSearchFlyout));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<MedicineSearchResult?> OpenHeaderResultInStockCommand => openHeaderResultInStockCommand ?? (openHeaderResultInStockCommand = new AsyncRelayCommand<MedicineSearchResult>(OpenHeaderResultInStockAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<MedicineSearchResult?> OpenHeaderResultInBillingCommand => openHeaderResultInBillingCommand ?? (openHeaderResultInBillingCommand = new RelayCommand<MedicineSearchResult>(OpenHeaderResultInBilling));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenLicenceCommand => openLicenceCommand ?? (openLicenceCommand = new AsyncRelayCommand(OpenLicenceAsync));

	public IRelayCommand OpenRenewLicenseCommand => openRenewLicenseCommand ?? (openRenewLicenseCommand = new RelayCommand(OpenRenewLicense));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenProfileCommand => openProfileCommand ?? (openProfileCommand = new AsyncRelayCommand(OpenProfileAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand DismissBarcodeToastCommand => dismissBarcodeToastCommand ?? (dismissBarcodeToastCommand = new RelayCommand(DismissBarcodeToast));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand DismissScannedItemPopoverCommand => dismissScannedItemPopoverCommand ?? (dismissScannedItemPopoverCommand = new RelayCommand(DismissScannedItemPopover));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddScannedItemToBillCommand => addScannedItemToBillCommand ?? (addScannedItemToBillCommand = new AsyncRelayCommand(AddScannedItemToBillAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> NavigateCommand => navigateCommand ?? (navigateCommand = new RelayCommand<string>(Navigate));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand UpdateNowCommand => updateNowCommand ?? (updateNowCommand = new AsyncRelayCommand(UpdateNowAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand RemindUpdateTomorrowCommand => remindUpdateTomorrowCommand ?? (remindUpdateTomorrowCommand = new RelayCommand(RemindUpdateTomorrow));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SelectRetailDeskModeCommand => selectRetailDeskModeCommand ?? (selectRetailDeskModeCommand = new RelayCommand(SelectRetailDeskMode));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SelectWholesaleDeskModeCommand => selectWholesaleDeskModeCommand ?? (selectWholesaleDeskModeCommand = new RelayCommand(SelectWholesaleDeskMode));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SelectCombinedDeskModeCommand => selectCombinedDeskModeCommand ?? (selectCombinedDeskModeCommand = new RelayCommand(SelectCombinedDeskMode));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenUserManualCommand => openUserManualCommand ?? (openUserManualCommand = new RelayCommand(OpenUserManual));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShortcutNewBillCommand => shortcutNewBillCommand ?? (shortcutNewBillCommand = new RelayCommand(ShortcutNewBill));

	public IAsyncRelayCommand ShortcutSaveAndPrintCommand => shortcutSaveAndPrintCommand ?? (shortcutSaveAndPrintCommand = new AsyncRelayCommand(ShortcutSaveAndPrintAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ShortcutSearchCommand => shortcutSearchCommand ?? (shortcutSearchCommand = new RelayCommand(ShortcutSearch));

	/// <summary>F3 — patient / retailer quick search.</summary>
	public IRelayCommand ShortcutFocusCustomerCommand => shortcutFocusCustomerCommand ?? (shortcutFocusCustomerCommand = new RelayCommand(ShortcutFocusCustomer));

	/// <summary>F4 — medicine search bar.</summary>
	public IRelayCommand ShortcutFocusMedicineCommand => shortcutFocusMedicineCommand ?? (shortcutFocusMedicineCommand = new RelayCommand(ShortcutFocusMedicine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand FocusSettingsSearchCommand => focusSettingsSearchCommand ?? (focusSettingsSearchCommand = new RelayCommand(FocusSettingsSearch, CanFocusSettingsSearch));

	public event EventHandler? LockRequested;

	public MainWindowViewModel(INavigationService navigationService, ILanguageService languageService, SettingsPageViewModel settingsPage, IServiceScopeFactory scopeFactory, CurrentSession currentSession, CatalogSearchViewModel catalogSearch, StockPageViewModel stockPage, StockTransferPageViewModel stockTransferPage, PurchasePageViewModel purchasePage, WholesaleCustomerPageViewModel wholesaleCustomerPage, WholesaleBillingPageViewModel wholesaleBillingPage, WholesalePricingPageViewModel wholesalePricingPage, WholesaleAccountsPageViewModel wholesaleAccountsPage, WholesaleReconciliationPageViewModel wholesaleReconciliationPage, DunningDashboardPageViewModel dunningDashboardPage, WholesaleReturnsPageViewModel wholesaleReturnsPage, StockInHandPageViewModel stockInHandPage, WholesaleReportsPageViewModel wholesaleReportsPage, ReportsPageViewModel reportsPage, GstReturnsPageViewModel gstReturnsPage, DashboardPageViewModel dashboardPage, BackupPageViewModel backupPage, SyncSettingsPageViewModel syncPage, RetailBillingViewModel retailBillingPage, IFilePickerService filePickerService, TabularExportService tabularExportService, DrugRecordsService drugRecordsService, StatutoryRegisterService statutoryRegisterService, SensitiveAccessService sensitiveAccessService, IConfirmationService confirmationService, SyncFolderExchangeService folderSync, IAccountDialogService accountDialogs, IUserManualDialogService userManualDialogs, IAppUpdateService appUpdates, CatalogueService catalogueService, GoogleDriveSyncService googleDrive, CloudBackupOrchestrator cloudBackup, ZeroConfigSyncCoordinator zeroConfigSync, UsbBackupSettingsStore usbBackupSettings, ActiveBillingDeskModeStore activeBillingDeskModeStore, ILogger<MainWindowViewModel> logger)
	{
		_navigationService = navigationService;
		_languageService = languageService;
		_settingsPage = settingsPage;
		_scopeFactory = scopeFactory;
		_currentSession = currentSession;
		_catalogSearch = catalogSearch;
		_stockPage = stockPage;
		_stockTransferPage = stockTransferPage;
		_purchasePage = purchasePage;
		_wholesaleCustomerPage = wholesaleCustomerPage;
		_wholesaleBillingPage = wholesaleBillingPage;
		_wholesalePricingPage = wholesalePricingPage;
		_wholesaleAccountsPage = wholesaleAccountsPage;
		_wholesaleReconciliationPage = wholesaleReconciliationPage;
		_dunningDashboardPage = dunningDashboardPage;
		_wholesaleReturnsPage = wholesaleReturnsPage;
		_stockInHandPage = stockInHandPage;
		_wholesaleReportsPage = wholesaleReportsPage;
		_reportsPage = reportsPage;
		_gstReturnsPage = gstReturnsPage;
		_dashboardPage = dashboardPage;
		_backupPage = backupPage;
		_syncPage = syncPage;
		_retailBillingPage = retailBillingPage;
		_filePickerService = filePickerService;
		_tabularExportService = tabularExportService;
		_drugRecordsService = drugRecordsService;
		_statutoryRegisterService = statutoryRegisterService;
		_sensitiveAccessService = sensitiveAccessService;
		_confirmationService = confirmationService;
		_folderSync = folderSync;
		_accountDialogs = accountDialogs;
		_userManualDialogs = userManualDialogs;
		_catalogueService = catalogueService;
		_googleDrive = googleDrive;
		_cloudBackup = cloudBackup;
		_zeroConfigSync = zeroConfigSync;
		_usbBackupSettings = usbBackupSettings;
		_activeBillingDeskModeStore = activeBillingDeskModeStore;
		_appUpdates = appUpdates;
		_logger = logger;
		CurrentPage = CreatePage(navigationService.CurrentSectionKey);
		UpdateSearchVisibility(navigationService.CurrentSectionKey);
		FlowDirection = languageService.FlowDirection;
		UpdateClock();
		_clockTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(30L)
		};
		_clockTimer.Tick += OnClockTick;
		_clockTimer.Start();
		_updatePollTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromHours(6)
		};
		_updatePollTimer.Tick += (object? _, EventArgs _) =>
		{
			CheckForAppUpdatesAsync();
		};
		_updatePollTimer.Start();
		_appUpdates.UpdateStateChanged += OnAppUpdateStateChanged;
		_navigationService.SectionChanged += OnSectionChanged;
		_languageService.LanguageChanged += OnLanguageChanged;
		_settingsPage.BusinessModeChanged += OnBusinessModeChanged;
		_folderSync.SettingsChanged += OnFolderSyncSettingsChanged;
		_catalogSearch.StockChanged += OnCatalogStockChanged;
		_catalogSearch.ViewStockRequested += OnViewStockRequested;
		_dashboardPage.DrillDownRequested += OnDashboardDrillDown;
		_googleDrive.ProgressChanged += OnGoogleDriveProgress;
		_retailBillingPage.BillSaved += OnRetailBillSaved;
		_usbBackupSettings.SettingsChanged += OnUsbBackupSettingsChanged;
		RefreshUsbBackupReminder();
	}

	private async void OnCatalogStockChanged(object? sender, EventArgs e)
	{
		try
		{
			await _stockPage.LoadAsync();
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Refreshing stock after adding stock failed.");
		}
	}

	private async void OnViewStockRequested(object? sender, string medicineName)
	{
		try
		{
			if (CurrentPage != _stockPage)
			{
				Navigate("Stock");
			}
			await _stockPage.ShowMedicineAsync(medicineName);
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Showing stock for {Medicine} failed.", medicineName);
		}
	}

	private void OnFolderSyncSettingsChanged(SyncFolderSettings settings)
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			FolderSyncWarning = settings.LastError ?? string.Empty;
			return;
		}
		dispatcher.BeginInvoke((Func<string>)(() =>
		{
			MainWindowViewModel mainWindowViewModel = this;
			string? obj = settings.LastError ?? string.Empty;
			string result = obj;
			mainWindowViewModel.FolderSyncWarning = obj;
			return result;
		}));
	}

	public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		FolderSyncWarning = (await _folderSync.GetSettingsAsync(cancellationToken)).LastError ?? string.Empty;
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmacyProfile pharmacyProfile = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.SingleAsync(cancellationToken);
		PharmacyName = pharmacyProfile.Name;
		_businessMode = pharmacyProfile.BusinessMode;
		_activeBillingDeskMode = _activeBillingDeskModeStore.Load(pharmacyProfile.BusinessMode);
		ApplyMode(pharmacyProfile.BusinessMode);
		_settingsPage.SelectedBusinessMode = pharmacyProfile.BusinessMode;
		try
		{
			ActiveBranchCode = (await scope.ServiceProvider.GetRequiredService<BranchService>().EnsureCurrentBranchAsync(cancellationToken)).Code;
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Could not resolve active branch for welcome banner.");
			ActiveBranchCode = "BR01-MAIN";
		}
		await RefreshAccessStatusAsync();
		RefreshUsbBackupReminder();
		await NavigateToAsync(LoadCurrentPageAsync);
		ShowWelcomeGreeting();
	}

	private void OnUsbBackupSettingsChanged(UsbBackupSettings _)
	{
		Dispatcher dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null || dispatcher.CheckAccess())
		{
			RefreshUsbBackupReminder();
		}
		else
		{
			dispatcher.BeginInvoke(new Action(RefreshUsbBackupReminder));
		}
	}

	private void RefreshUsbBackupReminder()
	{
		ShowUsbBackupReminder = _usbBackupSettings.IsReminderDue(DateTime.UtcNow);
	}

	private void UsbBackupReminderBackupNow()
	{
		Navigate("Sync");
		RefreshUsbBackupReminder();
	}

	private void UsbBackupReminderSnooze()
	{
		UsbBackupSettings usbBackupSettings = _usbBackupSettings.Load();
		_usbBackupSettings.Save(usbBackupSettings with
		{
			ReminderSnoozeUntilUtc = DateTime.UtcNow.AddDays(1.0)
		});
		RefreshUsbBackupReminder();
	}

	public void RefreshLoggedInUser()
	{
		ShowWelcomeGreeting();
	}

	public void ShowWelcomeGreeting()
	{
		_shiftStartedLocal = DateTime.Now;
		AppUser user = _currentSession.User;
		LoggedInUser = WelcomeGreeting.ResolveDisplayName(user);
		UserRoleLabel = WelcomeGreeting.ResolveRoleLabel(user);
		ShiftStartedDisplay = _shiftStartedLocal.ToString("h:mm tt");
		ProfileStatusSummary = $"Online / Ready{Environment.NewLine}Shift started {ShiftStartedDisplay}{Environment.NewLine}Branch {ActiveBranchCode}";
		WelcomeHeadline = WelcomeGreeting.BuildHeadline(DateTime.Now, user);
		WelcomeSubtitle = WelcomeGreeting.BuildRoleStoreLine(user, PharmacyName);
		string trialStatus = TrialStatus;
		EntitlementStatus entitlementStatus = ((trialStatus == "EXPIRED") ? EntitlementStatus.EXPIRED : ((!(trialStatus == "GRACE OFFLINE")) ? EntitlementStatus.SUBSCRIBED : EntitlementStatus.GRACE_OFFLINE));
		EntitlementStatus status = entitlementStatus;
		WelcomeStatusLine = WelcomeGreeting.BuildStatusLine(status);
		ShowWelcomeToast = true;
		AutoDismissWelcomeToastAsync();
	}

	private void DismissWelcomeToast()
	{
		_welcomeDismissCts?.Cancel();
		ShowWelcomeToast = false;
	}

	private void OpenProfileStatusPopup()
	{
		IsProfileStatusPopupOpen = true;
	}

	private void CloseProfileStatusPopup()
	{
		IsProfileStatusPopupOpen = false;
	}

	private async Task AutoDismissWelcomeToastAsync()
	{
		_welcomeDismissCts?.Cancel();
		_welcomeDismissCts = new CancellationTokenSource();
		CancellationToken token = _welcomeDismissCts.Token;
		try
		{
			await Task.Delay(TimeSpan.FromSeconds(4.5), token);
			if (!token.IsCancellationRequested)
			{
				ShowWelcomeToast = false;
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	public void StartBackgroundCatalogImportAsync()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_catalog.csv");
		string path2 = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_info.csv");
		if (!File.Exists(path) && !File.Exists(path2))
		{
			return;
		}
		Task.Run(async () =>
		{
			try
			{
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					CatalogueImportProgress = 0.0;
					CatalogueImportStatus = "Indexing 253,973 medicines in background...";
					IsImportingCatalogue = true;
				});
				await _catalogueService.EnsureCatalogueSeededAsync((CatalogueSeedProgress progress) =>
				{
					Application.Current?.Dispatcher.InvokeAsync(() =>
					{
						CatalogueImportProgress = progress.Percent;
						CatalogueImportStatus = $"{progress.ProcessedItems:N0} records processed...";
					});
				});
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					CatalogueImportProgress = 100.0;
					CatalogueImportStatus = "Database up to date ✓";
				});
				await Task.Delay(1500);
				await Application.Current.Dispatcher.InvokeAsync(() => IsImportingCatalogue = false);
			}
			catch (Exception exception)
			{
				_logger.LogError(exception, "Background catalogue import failed.");
				await Application.Current.Dispatcher.InvokeAsync(() =>
				{
					CatalogueImportStatus = "Import paused — resume from Import medicine data.";
					CatalogueImportProgress = Math.Max(CatalogueImportProgress, 0.0);
				});
			}
		});
	}

	private void OnGoogleDriveProgress(GoogleDriveProgress progress)
	{
	}

	private void OnRetailBillSaved(object? sender, EventArgs e)
	{
		MaybeZeroConfigSyncAfterBillAsync();
	}

	public async Task SyncGoogleDriveOnExitAsync()
	{
		try
		{
			await Task.Run(() => _cloudBackup.UploadToSelectedProvidersAsync(triggeredByBillSave: false, CancellationToken.None));
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Cloud automatic upload on exit failed.");
		}
	}

	private async Task MaybeZeroConfigSyncAfterBillAsync()
	{
		try
		{
			await Task.Run(() => _zeroConfigSync.OnBillOrLedgerSavedAsync());
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Zero-config sync after bill save failed.");
		}
	}

	private async Task AskAssistantAsync()
	{
		string text = (AssistantQuery ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			AssistantMessage = "Try: sales today, sales this month, expiring batches, low stock, top selling, gst, profit, h1 sales, backup.";
			IsHeaderSearchFlyoutOpen = false;
			return;
		}
		AssistantAction assistantAction = AssistantQueryInterpreter.Interpret(text, DateOnly.FromDateTime(DateTime.Today));
		if (!assistantAction.Recognised || assistantAction.SectionKey == null)
		{
			await RunHeaderMedicineSearchAsync(text);
			return;
		}
		IsHeaderSearchFlyoutOpen = false;
		AssistantMessage = assistantAction.Message;
		bool flag = assistantAction.ReportKey != null && _navigationService.CurrentSectionKey == assistantAction.SectionKey;
		if (assistantAction.ReportKey != null)
		{
			DateOnly? dateOnly = assistantAction.From;
			if (dateOnly.HasValue)
			{
				DateOnly valueOrDefault = dateOnly.GetValueOrDefault();
				dateOnly = assistantAction.To;
				if (dateOnly.HasValue)
				{
					DateOnly valueOrDefault2 = dateOnly.GetValueOrDefault();
					if (!_reportsPage.PrepareAssistantRequest(assistantAction.ReportKey, valueOrDefault, valueOrDefault2))
					{
						AssistantMessage = "Try: sales today, sales this month, expiring batches, low stock, top selling, gst, profit, h1 sales, backup.";
						return;
					}
				}
			}
		}
		Navigate(assistantAction.SectionKey);
		if (flag)
		{
			await _reportsPage.RefreshNowAsync();
		}
		AssistantQuery = string.Empty;
	}

	private async Task RunHeaderMedicineSearchAsync(string query)
	{
		try
		{
			_catalogSearch.Query = query;
			await _catalogSearch.SearchCommand.ExecuteAsync(null);
			bool flag = _catalogSearch.InStockResults.Count > 0 || _catalogSearch.CatalogResults.Count > 0;
			IsHeaderSearchFlyoutOpen = flag || query.Length >= 2;
			AssistantMessage = (flag ? $"Found {_catalogSearch.InStockResults.Count} in stock, {_catalogSearch.CatalogResults.Count} in catalogue." : ("No medicine match for '" + query + "'. Try: sales today, low stock, expiring batches, gst."));
		}
		catch (Exception exception)
		{
			_logger.LogWarning(exception, "Header medicine search failed.");
			IsHeaderSearchFlyoutOpen = false;
			AssistantMessage = "Search could not be completed. Try again.";
		}
	}

	private void CloseHeaderSearchFlyout()
	{
		IsHeaderSearchFlyoutOpen = false;
	}

	private async Task OpenHeaderResultInStockAsync(MedicineSearchResult? medicine)
	{
		string name = medicine?.Name ?? AssistantQuery;
		if (!string.IsNullOrWhiteSpace(name))
		{
			IsHeaderSearchFlyoutOpen = false;
			Navigate("Stock");
			await _stockPage.ShowMedicineAsync(name.Trim());
			AssistantMessage = "Stock filtered for " + name.Trim() + ".";
			AssistantQuery = string.Empty;
		}
	}

	private void OpenHeaderResultInBilling(MedicineSearchResult? medicine)
	{
		string text = medicine?.Name ?? AssistantQuery;
		if (!string.IsNullOrWhiteSpace(text))
		{
			IsHeaderSearchFlyoutOpen = false;
			if (_activeBillingDeskMode == ActiveBillingDeskMode.Wholesale && CanUseWholesaleBilling)
			{
				Navigate("WholesaleBilling");
				AssistantMessage = "Wholesale billing: " + text.Trim() + ".";
				AssistantQuery = string.Empty;
			}
			else if (CanUseRetailBilling)
			{
				Navigate("Billing");
				_retailBillingPage.SeedItemSearch(text.Trim());
				AssistantMessage = "Billing search: " + text.Trim() + ".";
				AssistantQuery = string.Empty;
			}
		}
	}

	private void OnDashboardDrillDown(DashboardTarget target)
	{
		try
		{
			DateOnly dateOnly = DateOnly.FromDateTime(DateTime.Today);
			switch (target)
			{
			case DashboardTarget.SalesToday:
				OpenReport("Sales", dateOnly, dateOnly);
				break;
			case DashboardTarget.SalesMonth:
				OpenReport("Sales", new DateOnly(dateOnly.Year, dateOnly.Month, 1), dateOnly);
				break;
			case DashboardTarget.ExpiringStock:
				_stockPage.ShowStatus("Near expiry");
				Navigate("Stock");
				break;
			case DashboardTarget.LowStock:
				_stockPage.ShowStatus("Shortage");
				Navigate("Stock");
				break;
			case DashboardTarget.WholesaleOutstanding:
				Navigate((_businessMode == BusinessMode.Retail) ? "Customers" : "Accounts");
				break;
			case DashboardTarget.RegisterEntries:
				if (_businessMode == BusinessMode.Wholesaler)
				{
					_dashboardPage.ErrorMessage = "Statutory registers are not used in wholesaler-only mode.";
				}
				else
				{
					Navigate("Registers");
				}
				break;
			}
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Dashboard drill-down to {Target} failed.", target);
		}
	}

	private void OpenReport(string reportKey, DateOnly from, DateOnly to)
	{
		if (!_reportsPage.PrepareAssistantRequest(reportKey, from, to))
		{
			_dashboardPage.ErrorMessage = "That report is not available.";
		}
		else
		{
			Navigate("Reports");
		}
	}

	private void OpenRenewLicense()
	{
		try
		{
			Window? owner = Application.Current?.MainWindow;
			UpgradeWindow upgradeWindow = new UpgradeWindow();
			if (owner != null)
			{
				upgradeWindow.Owner = owner;
			}

			if (upgradeWindow.ShowDialog() == true)
			{
				LicenseManager.InvalidateCache();
				_ = RefreshAccessStatusAsync();
			}
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Opening the renew licence dialog failed.");
		}
	}

	private async Task OpenLicenceAsync()
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			EntitlementStatus entitlement = await scope.ServiceProvider.GetRequiredService<AccessService>().GetStatusAsync();
			string offlineStatus = LicenseManager.GetStatusSummary();
			string machineId = LicenseManager.GetMachineId();
			int? trialLeft = LicenseManager.GetTrialDaysRemaining();
			DateTime? licensedUntil = LicenseManager.GetLicensedUntilUtc();

			var (headline, detail) = entitlement switch
			{
				EntitlementStatus.SUBSCRIBED when LicenseManager.IsAppActivated() => (
					offlineStatus,
					$"Annual offline licence is active on this Win32 workstation.\nMachine ID: {machineId}\nShare this ID to renew (PBILL-XXXX-XXXX-XXXX-XXXX). Renewal extends ValidUntil by 365 days — no reinstall."),
				EntitlementStatus.SUBSCRIBED => (
					offlineStatus,
					$"7-Day Free Trial is running on this PC.\nMachine ID: {machineId}\nUse Copy ID on Upgrade, then email pharma.bill26@gmail.com for an annual key."),
				EntitlementStatus.GRACE_OFFLINE => (
					"Offline grace period",
					"Connect when possible so backups and sync can refresh. Local offline licence status: " + offlineStatus),
				_ when !LicenseManager.HasFullAccess() => (
					"Annual License Expired — read-only",
					$"New retail/wholesale invoices and stock inward are blocked until you renew.\nMachine ID: {machineId}\nPast invoices, Drug Records, stock, GST and reports stay readable.\n{offlineStatus}"),
				_ => (
					"Billing is read-only",
					"A drug licence required for the active business mode is missing or expired. You can still view, search, export, print, back up and restore everything.\n" + offlineStatus),
			};

			string? endsOn = licensedUntil?.ToString("dd-MMM-yyyy")
				?? (trialLeft is > 0 ? DateTime.Today.AddDays(trialLeft.Value).ToString("dd-MMM-yyyy") : null);
			_accountDialogs.ShowLicence(new LicenceSummary(offlineStatus, headline, detail, LicenseManager.AnnualGrantDays, trialLeft, endsOn));
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Opening the licence dialog failed.");
		}
	}

	private async Task OpenProfileAsync()
	{
		_ = 1;
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().SingleAsync();
			List<string> list = (from item in await context.LicenceRecords.AsNoTracking().ToListAsync()
				orderby item.LicenceType, item.LicenceNumber
				select item).Select((LicenceRecord item) =>
			{
				string licenceType = item.LicenceType;
				string licenceNumber = item.LicenceNumber;
				DateOnly? expiresOn = item.ExpiresOn;
				string text2;
				if (expiresOn.HasValue)
				{
					DateOnly valueOrDefault = expiresOn.GetValueOrDefault();
					text2 = $" (expires {valueOrDefault:dd-MMM-yyyy})";
				}
				else
				{
					text2 = string.Empty;
				}
				return licenceType + ": " + licenceNumber + text2;
			}).ToList();
			AppUser user = _currentSession.User;
			string[] array = new string[2] { profile.CompetentPersonName, null };
			string competentPersonRegistrationNumber = profile.CompetentPersonRegistrationNumber;
			array[1] = ((competentPersonRegistrationNumber != null && competentPersonRegistrationNumber.Length > 0) ? ("Reg. " + competentPersonRegistrationNumber) : null);
			string text = string.Join(" - ", array.Where((string value) => !string.IsNullOrWhiteSpace(value)));
			ProfileSummary summary = new ProfileSummary(user?.DisplayName ?? LoggedInUser, user?.Role.ToString() ?? string.Empty, user?.UserName ?? string.Empty, user?.Phone ?? "No phone recorded", user?.Email ?? "No email recorded", profile.Name, profile.Address ?? "No address recorded", profile.Phone ?? string.Empty, (text.Length == 0) ? "Not recorded" : text, (list.Count == 0) ? new List<string>(1) { "No licences recorded" } : list);
			if (_accountDialogs.ShowProfile(summary))
			{
				LockRequested?.Invoke(this, EventArgs.Empty);
			}
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Opening the pharmacist profile failed.");
		}
	}

	public void ShowItemQuickToast(string message)
	{
		ShowBarcodeToast(message);
	}

	public void ClearScannedItemPopover()
	{
		ScannedItemPopover = null;
	}

	public async Task HandleBarcodeScannedAsync(string barcode, bool showLookupToast = true)
	{
		if (string.IsNullOrWhiteSpace(barcode))
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			RetailStockChoice choice = await scope.ServiceProvider.GetRequiredService<RetailBillingService>().GetByBarcodeAsync(barcode);
			if ((object)choice == null)
			{
				if (showLookupToast)
				{
					ShowBarcodeToast("Barcode " + barcode.Trim() + " not found in inventory.");
				}
				ScannedItemPopover = null;
				return;
			}
			string preferredBilling = PreferredBillingSection() ?? "Billing";
			bool onRetailBilling = string.Equals(_navigationService.CurrentSectionKey, "Billing", StringComparison.OrdinalIgnoreCase);
			bool onWholesaleBilling = string.Equals(_navigationService.CurrentSectionKey, "WholesaleBilling", StringComparison.OrdinalIgnoreCase);
			if (onWholesaleBilling || (preferredBilling == "WholesaleBilling" && CanUseWholesaleBilling && _retailBillingPage.BillItems.Count == 0))
			{
				if (!onWholesaleBilling)
				{
					Navigate("WholesaleBilling");
				}
				ShowBarcodeToast("Scanned: " + choice.DrugName + " — add from Wholesale Invoice stock picker.");
				ScannedItemPopover = choice;
				return;
			}

			bool flag = onRetailBilling;
			if (flag || _retailBillingPage.BillItems.Count > 0)
			{
				if (!flag)
				{
					Navigate("Billing");
				}
				if (!(await _retailBillingPage.TryAddByBarcodeAsync(barcode)))
				{
					if (showLookupToast)
					{
						ShowBarcodeToast("Barcode " + barcode.Trim() + " not found in inventory.");
					}
				}
				else
				{
					ScannedItemPopover = null;
					ShowBarcodeToast("Added to bill: " + choice.DrugName);
				}
				return;
			}
			ScannedItemPopover = choice;
			if (showLookupToast)
			{
				ShowBarcodeToast("Scanned: " + choice.DrugName);
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Barcode scan failed for {Barcode}", barcode);
			ShowBarcodeToast("Barcode lookup failed: " + ex.Message);
		}
	}

	private void DismissBarcodeToast()
	{
		BarcodeToastMessage = string.Empty;
	}

	private void DismissScannedItemPopover()
	{
		ScannedItemPopover = null;
	}

	private async Task AddScannedItemToBillAsync()
	{
		if ((object)ScannedItemPopover != null)
		{
			string barcode = ((!string.IsNullOrWhiteSpace(ScannedItemPopover.Barcode)) ? ScannedItemPopover.Barcode : ScannedItemPopover.BatchNo);
			string billingSection = PreferredBillingSection() ?? "Billing";
			Navigate(billingSection);
			if (billingSection == "WholesaleBilling")
			{
				ShowBarcodeToast("Opened Wholesale Invoice — select batch for " + ScannedItemPopover.DrugName + ".");
				ScannedItemPopover = null;
				return;
			}

			if (await _retailBillingPage.TryAddByBarcodeAsync(barcode))
			{
				ShowBarcodeToast("Added to bill: " + ScannedItemPopover.DrugName);
			}
			ScannedItemPopover = null;
		}
	}

	private void ShowBarcodeToast(string message)
	{
		BarcodeToastMessage = message;
		DismissBarcodeToastAfterDelayAsync();
	}

	private async Task DismissBarcodeToastAfterDelayAsync()
	{
		string message = BarcodeToastMessage;
		await Task.Delay(4500);
		if (BarcodeToastMessage == message)
		{
			BarcodeToastMessage = string.Empty;
		}
	}

	private void Navigate(string? sectionKey)
	{
		if (string.IsNullOrWhiteSpace(sectionKey))
		{
			throw new ArgumentException("A navigation section must be provided.", "sectionKey");
		}

		// Desk mode owns which billing surface is shown — never open Retail billing while WS Mode is active.
		string resolvedKey = ResolveBillingNavigation(sectionKey);

		bool flag = IsCriticalUpdateRequired;
		if (flag)
		{
			bool flag2 = ((resolvedKey == "Billing" || resolvedKey == "WholesaleBilling") ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			PromptCriticalUpdateAsync();
		}
		else
		{
			_navigationService.Navigate(resolvedKey);
		}
	}

	private string ResolveBillingNavigation(string sectionKey)
	{
		// Exclusive desk modes always land on that mode's billing surface (no duplicate entry points).
		if (sectionKey is "Billing" or "WholesaleBilling")
		{
			if (_activeBillingDeskMode == ActiveBillingDeskMode.Wholesale && CanUseWholesaleBilling)
			{
				return "WholesaleBilling";
			}

			if (_activeBillingDeskMode == ActiveBillingDeskMode.Retail && CanUseRetailBilling)
			{
				return "Billing";
			}
		}

		return sectionKey;
	}

	public Task CheckForAppUpdatesAsync(bool force = false)
	{
		return CheckForAppUpdatesInternalAsync(force);
	}

	private async Task CheckForAppUpdatesInternalAsync(bool force)
	{
		try
		{
			ApplyUpdateAvailability(await Task.Run(async () => await _appUpdates.CheckForUpdatesAsync(force).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: true));
		}
		catch (Exception exception)
		{
			_logger.LogDebug(exception, "Background update check skipped.");
		}
	}

	private void OnAppUpdateStateChanged(object? sender, AppUpdateAvailability result)
	{
		Application.Current?.Dispatcher.Invoke(() =>
		{
			ApplyUpdateAvailability(result);
		});
	}

	private void ApplyUpdateAvailability(AppUpdateAvailability result)
	{
		IsCriticalUpdateRequired = result.IsUpdateAvailable && result.IsCritical;
		UpdateBannerText = result.StatusMessage;
		ShowUpdateBanner = result.IsUpdateAvailable && (IsCriticalUpdateRequired || !_appUpdates.IsSnoozed());
		if (IsCriticalUpdateRequired && !_criticalUpdateDialogShown)
		{
			PromptCriticalUpdateAsync();
		}
	}

	private async Task PromptCriticalUpdateAsync()
	{
		_criticalUpdateDialogShown = true;
		ShowUpdateBanner = true;
		string message = (string.IsNullOrWhiteSpace(UpdateBannerText) ? "A critical PharmaBill update is required before billing can continue." : UpdateBannerText);
		Window owner = Application.Current?.MainWindow;
		CriticalUpdateWindow criticalUpdateWindow = new CriticalUpdateWindow(message)
		{
			Owner = owner
		};
		if (criticalUpdateWindow.ShowDialog() == true && criticalUpdateWindow.UpdateRequested)
		{
			await UpdateNowAsync();
		}
		else
		{
			Application.Current?.Shutdown();
		}
	}

	private async Task UpdateNowAsync()
	{
		try
		{
			await _appUpdates.LaunchUpdateAsync();
		}
		catch (Exception ex)
		{
			_confirmationService.NotifyError("Update", "Could not start the update: " + ex.Message);
		}
	}

	private void RemindUpdateTomorrow()
	{
		if (IsCriticalUpdateRequired)
		{
			PromptCriticalUpdateAsync();
			return;
		}
		_appUpdates.RemindTomorrow();
		ShowUpdateBanner = false;
	}

	private void SelectRetailDeskMode()
	{
		if (CanUseRetailBilling)
		{
			SetActiveBillingDeskMode(ActiveBillingDeskMode.Retail);
			Navigate("Billing");
		}
	}

	private void SelectWholesaleDeskMode()
	{
		if (CanUseWholesaleBilling)
		{
			SetActiveBillingDeskMode(ActiveBillingDeskMode.Wholesale);
			Navigate("WholesaleBilling");
		}
	}

	private void SelectCombinedDeskMode()
	{
		if (CanUseCombinedDeskMode)
		{
			// Sidebar shows both Retail Billing and Wholesale Invoice; keep the current page.
			SetActiveBillingDeskMode(ActiveBillingDeskMode.Combined);
		}
	}

	private void OpenUserManual()
	{
		_userManualDialogs.ShowInteractiveGuide();
	}

	private void ShortcutNewBill()
	{
		if (CurrentPage is WholesaleBillingPageViewModel wholesale)
		{
			if (wholesale.NewBillCommand.CanExecute(null))
			{
				wholesale.NewBillCommand.Execute(null);
			}
			return;
		}

		if (CurrentPage is RetailBillingViewModel retail)
		{
			if (retail.NewBillCommand.CanExecute(null))
			{
				retail.NewBillCommand.Execute(null);
			}
			return;
		}

		if (_activeBillingDeskMode == ActiveBillingDeskMode.Wholesale || (!CanUseRetailBilling && CanUseWholesaleBilling))
		{
			if (CanUseWholesaleBilling)
			{
				Navigate("WholesaleBilling");
				if (_wholesaleBillingPage.NewBillCommand.CanExecute(null))
				{
					_wholesaleBillingPage.NewBillCommand.Execute(null);
				}
			}
		}
		else if (CanUseRetailBilling)
		{
			Navigate("Billing");
			if (_retailBillingPage.NewBillCommand.CanExecute(null))
			{
				_retailBillingPage.NewBillCommand.Execute(null);
			}
		}
	}

	private async Task ShortcutSaveAndPrintAsync()
	{
		if (CurrentPage is WholesaleBillingPageViewModel wholesale)
		{
			if (wholesale.SaveAndPrintCommand.CanExecute(null))
			{
				await wholesale.SaveAndPrintCommand.ExecuteAsync(null);
			}

			return;
		}

		if (CurrentPage is RetailBillingViewModel retail && retail.SaveAndPrintCommand.CanExecute(null))
		{
			await retail.SaveAndPrintCommand.ExecuteAsync(null);
			return;
		}

		// Desk mode may have focus elsewhere — route to the preferred billing page first.
		string? billing = PreferredBillingSection();
		if (billing == "WholesaleBilling" && CanUseWholesaleBilling)
		{
			Navigate("WholesaleBilling");
			if (_wholesaleBillingPage.SaveAndPrintCommand.CanExecute(null))
			{
				await _wholesaleBillingPage.SaveAndPrintCommand.ExecuteAsync(null);
			}
		}
		else if (CanUseRetailBilling && _retailBillingPage.SaveAndPrintCommand.CanExecute(null))
		{
			Navigate("Billing");
			await _retailBillingPage.SaveAndPrintCommand.ExecuteAsync(null);
		}
	}

	private void ShortcutSearch()
	{
		// Legacy alias: F3 historically focused medicine search; now routes to customer, F4 to medicine.
		ShortcutFocusCustomer();
	}

	private void ShortcutFocusCustomer()
	{
		EnsureBillingSectionForShortcuts();
		if (CurrentPage is WholesaleBillingPageViewModel wholesale)
		{
			wholesale.FocusCustomerCommand.Execute(null);
			return;
		}

		if (CurrentPage is RetailBillingViewModel retail)
		{
			retail.FocusPatientCommand.Execute(null);
		}
	}

	private void ShortcutFocusMedicine()
	{
		EnsureBillingSectionForShortcuts();
		if (CurrentPage is WholesaleBillingPageViewModel wholesale)
		{
			wholesale.FocusMedicineCommand.Execute(null);
			return;
		}

		if (CurrentPage is RetailBillingViewModel retail)
		{
			retail.FocusMedicineCommand.Execute(null);
			return;
		}

		if (CurrentPage is PurchasePageViewModel purchase)
		{
			purchase.RequestFocusMedicineSearch();
		}
	}

	private void EnsureBillingSectionForShortcuts()
	{
		string text = _navigationService.CurrentSectionKey;
		bool onBillingSurface = text is "Billing" or "WholesaleBilling" or "Stock" or "Purchases" or "StockTransfer";
		if (!onBillingSurface)
		{
			Navigate(PreferredBillingSection() ?? "Billing");
		}
	}

	private void FocusSettingsSearch()
	{
		if (_settingsPage.FocusSettingsSearchCommand.CanExecute(null))
		{
			_settingsPage.FocusSettingsSearchCommand.Execute(null);
		}
	}

	private bool CanFocusSettingsSearch()
	{
		return CurrentPage is SettingsPageViewModel;
	}

	private async void OnSectionChanged(object? sender, EventArgs e)
	{
		string section = _navigationService.CurrentSectionKey;
		OnPropertyChanged("CurrentSectionKey");
		if (section == "Sync")
		{
			RefreshUsbBackupReminder();
		}
		await NavigateToAsync(async () =>
		{
			CurrentPage = CreatePage(section);
			FocusSettingsSearchCommand.NotifyCanExecuteChanged();
			UpdateSearchVisibility(section);
			await LoadCurrentPageAsync();
		});
	}

	private void UpdateSearchVisibility(string section)
	{
		CatalogSearchViewModel catalogSearch = _catalogSearch;
		bool isVisible;
		switch (section)
		{
		case "Billing":
		case "WholesaleBilling":
		case "Stock":
		case "Purchases":
		case "StockTransfer":
			isVisible = true;
			break;
		default:
			isVisible = false;
			break;
		}
		catalogSearch.IsVisible = isVisible;
	}

	private void OnLanguageChanged(object? sender, EventArgs e)
	{
		FlowDirection = _languageService.FlowDirection;
		RebuildNavigationItems(_businessMode);
		if (CurrentPage is SectionPageViewModel sectionPageViewModel && !(CurrentPage is SettingsPageViewModel))
		{
			sectionPageViewModel.Title = ResolveNavTitle(_navigationService.CurrentSectionKey);
		}
	}

	private async void OnBusinessModeChanged(object? sender, EventArgs e)
	{
		try
		{
			await InitializeAsync();
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Refreshing the application after a business-mode change failed.");
		}
	}

	private async void OnClockTick(object? sender, EventArgs e)
	{
		UpdateClock();
		try
		{
			await RefreshAccessStatusAsync();
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Refreshing entitlement status failed.");
		}
	}

	private async Task RefreshAccessStatusAsync()
	{
		try
		{
			LicenseManager.RecordRunTimestamp();
		}
		catch
		{
		}

		using IServiceScope scope = _scopeFactory.CreateScope();
		EntitlementStatus entitlementStatus = await scope.ServiceProvider.GetRequiredService<AccessService>().GetStatusAsync();
		TrialStatus = entitlementStatus switch
		{
			EntitlementStatus.SUBSCRIBED => "LICENSED", 
			EntitlementStatus.GRACE_OFFLINE => "GRACE OFFLINE", 
			EntitlementStatus.TRIAL => "LICENSED", 
			_ => "EXPIRED", 
		};
		IsReadOnly = entitlementStatus == EntitlementStatus.EXPIRED;
		string trialStatusToolTip;
		switch (entitlementStatus)
		{
		case EntitlementStatus.TRIAL:
		case EntitlementStatus.SUBSCRIBED:
			trialStatusToolTip = LicenseManager.GetStatusSummary();
			break;
		case EntitlementStatus.GRACE_OFFLINE:
			trialStatusToolTip = "Offline grace — connect when possible to refresh backups/sync";
			break;
		default:
			trialStatusToolTip = LicenseManager.HasFullAccess()
				? "Drug licence for the active mode is missing or expired — billing is read-only"
				: "Annual license expired — enter renewal key to create new bills (existing records stay readable)";
			break;
		}
		TrialStatusToolTip = trialStatusToolTip;
		OnPropertyChanged(nameof(ReadOnlyBanner));
		OnPropertyChanged(nameof(ShowAnnualLicenseWarning));
		OnPropertyChanged(nameof(AnnualLicenseWarningText));
	}

	private void ApplyMode(BusinessMode mode)
	{
		CanUseRetailBilling = mode != BusinessMode.Wholesaler;
		CanUseWholesaleBilling = mode != BusinessMode.Retail;
		_activeBillingDeskMode = ActiveBillingDeskModeStore.Clamp(_activeBillingDeskMode, mode);
		RefreshModeBadge();
		OnPropertyChanged("CanUseCombinedDeskMode");
		bool flag = mode == BusinessMode.Wholesaler;
		if (flag)
		{
			bool flag2;
			switch (_navigationService.CurrentSectionKey)
			{
			case "Billing":
			case "Registers":
			case "DrugRecords":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag)
		{
			_navigationService.Navigate("WholesaleBilling");
		}
		else if (mode == BusinessMode.Retail
		         && string.Equals(_navigationService.CurrentSectionKey, "WholesaleBilling", StringComparison.OrdinalIgnoreCase))
		{
			_navigationService.Navigate("Billing");
		}
		else if (_activeBillingDeskMode == ActiveBillingDeskMode.Wholesale
		         && string.Equals(_navigationService.CurrentSectionKey, "Billing", StringComparison.OrdinalIgnoreCase)
		         && CanUseWholesaleBilling)
		{
			_navigationService.Navigate("WholesaleBilling");
		}
		else if (_activeBillingDeskMode == ActiveBillingDeskMode.Retail
		         && string.Equals(_navigationService.CurrentSectionKey, "WholesaleBilling", StringComparison.OrdinalIgnoreCase)
		         && CanUseRetailBilling)
		{
			_navigationService.Navigate("Billing");
		}

		RebuildNavigationItems(mode);
	}

	/// <summary>
	/// Option A: one primary billing sidebar entry that matches the active desk mode.
	/// Combined (2-in-1) exposes both Retail Billing and Wholesale Invoice as distinct entries.
	/// </summary>
	private void RebuildNavigationItems(BusinessMode mode)
	{
		NavigationItems.Clear();
		NavigationItems.Add(new NavigationItem("Dashboard", "Dashboard"));
		AddPrimaryBillingNavigationItems();
		NavigationItems.Add(new NavigationItem("Stock", _languageService.GetString("NavStock")));
		NavigationItems.Add(new NavigationItem("StockTransfer", "Stock transfer"));
		NavigationItems.Add(new NavigationItem("Purchases", _languageService.GetString("NavPurchases")));
		NavigationItems.Add(new NavigationItem("Customers", _languageService.GetString("NavCustomers")));
		if (mode != BusinessMode.Retail)
		{
			NavigationItems.Add(new NavigationItem("Pricing", "Pack levels & pricing"));
			NavigationItems.Add(new NavigationItem("Accounts", "Accounts"));
			NavigationItems.Add(new NavigationItem("BankReconciliation", "Bank & Ledger Reconciliation"));
			NavigationItems.Add(new NavigationItem("CollectionsDunning", "Collections & Smart Dunning"));
			NavigationItems.Add(new NavigationItem("Returns", "Returns & credit notes"));
			NavigationItems.Add(new NavigationItem("StockInHand", "Stock in hand"));
		}
		if (mode != BusinessMode.Wholesaler)
		{
			NavigationItems.Add(new NavigationItem("Registers", _languageService.GetString("NavRegisters")));
			NavigationItems.Add(new NavigationItem("DrugRecords", _languageService.GetString("NavDrugRecords")));
		}
		NavigationItems.Add(new NavigationItem("Reports", _languageService.GetString("NavReports")));
		NavigationItems.Add(new NavigationItem("GstReturns", "GST Returns"));
		NavigationItems.Add(new NavigationItem("Backup", "Backup & move PC"));
		NavigationItems.Add(new NavigationItem("Sync", "Sync"));
		NavigationItems.Add(new NavigationItem("Inspector", _languageService.GetString("NavInspector")));
	}

	private void AddPrimaryBillingNavigationItems()
	{
		bool showRetail = CanUseRetailBilling;
		bool showWholesale = CanUseWholesaleBilling;

		switch (_activeBillingDeskMode)
		{
		case ActiveBillingDeskMode.Retail:
			showWholesale = false;
			break;
		case ActiveBillingDeskMode.Wholesale:
			showRetail = false;
			break;
		}

		if (showRetail)
		{
			NavigationItems.Add(new NavigationItem("Billing", _languageService.GetString("NavRetailBilling")));
		}

		if (showWholesale)
		{
			NavigationItems.Add(new NavigationItem("WholesaleBilling", _languageService.GetString("NavWholesaleInvoice")));
		}
	}

	private string ResolveNavTitle(string sectionKey)
	{
		return sectionKey switch
		{
			"Billing" => _languageService.GetString("NavRetailBilling"),
			"WholesaleBilling" => _languageService.GetString("NavWholesaleInvoice"),
			_ => _languageService.GetString("Nav" + sectionKey),
		};
	}

	private void SetActiveBillingDeskMode(ActiveBillingDeskMode mode)
	{
		ActiveBillingDeskMode mode2 = (_activeBillingDeskMode = ActiveBillingDeskModeStore.Clamp(mode, _businessMode));
		_activeBillingDeskModeStore.Save(mode2);
		RefreshModeBadge();
		RebuildNavigationItems(_businessMode);
	}

	private void RefreshModeBadge()
	{
		(ModeStatus, ModeBadgeKind) = _activeBillingDeskMode switch
		{
			ActiveBillingDeskMode.Retail => ("You are in RTL Mode", "Retail"), 
			ActiveBillingDeskMode.Wholesale => ("You are in WS Mode", "Wholesale"), 
			_ => ("MODE: 2-IN-1 (RTL + WS)", "Combined"), 
		};
		OnPropertyChanged("IsRetailDeskActive");
		OnPropertyChanged("IsWholesaleDeskActive");
		OnPropertyChanged("IsCombinedDeskActive");
	}

	private string? PreferredBillingSection()
	{
		if (_activeBillingDeskMode == ActiveBillingDeskMode.Wholesale && CanUseWholesaleBilling)
		{
			return "WholesaleBilling";
		}
		if (_activeBillingDeskMode == ActiveBillingDeskMode.Retail && CanUseRetailBilling)
		{
			return "Billing";
		}
		if (CanUseRetailBilling)
		{
			return "Billing";
		}
		if (CanUseWholesaleBilling)
		{
			return "WholesaleBilling";
		}
		return null;
	}

	private object CreatePage(string sectionKey)
	{
		return sectionKey switch
		{
			"Dashboard" => (object)_dashboardPage, 
			"Backup" => _backupPage, 
			"Sync" => _syncPage, 
			"Settings" => _settingsPage, 
			"Stock" => _stockPage, 
			"StockTransfer" => _stockTransferPage, 
			"Purchases" => _purchasePage, 
			"Customers" => _wholesaleCustomerPage, 
			"WholesaleBilling" => _wholesaleBillingPage, 
			"Pricing" => _wholesalePricingPage, 
			"Accounts" => _wholesaleAccountsPage, 
			"BankReconciliation" => _wholesaleReconciliationPage, 
			"CollectionsDunning" => _dunningDashboardPage, 
			"Returns" => _wholesaleReturnsPage, 
			"StockInHand" => _stockInHandPage, 
			"Billing" => _retailBillingPage, 
			"Reports" => _reportsPage, 
			"GstReturns" => _gstReturnsPage, 
			"DrugRecords" => new DrugRecordsPageViewModel(_drugRecordsService, _filePickerService, _tabularExportService, _sensitiveAccessService, _confirmationService, _currentSession), 
			"Registers" => new StatutoryRegistersPageViewModel(_statutoryRegisterService, _sensitiveAccessService, _confirmationService, _filePickerService, _tabularExportService, _currentSession), 
			"Inspector" => new InspectorPageViewModel(_scopeFactory, _sensitiveAccessService), 
			_ => new SectionPageViewModel(ResolveNavTitle(sectionKey)), 
		};
	}

	private Task LoadCurrentPageAsync()
	{
		if (!(CurrentPage is ILoadablePage loadablePage))
		{
			return Task.CompletedTask;
		}
		return loadablePage.LoadAsync();
	}

	public async Task NavigateToAsync(Func<Task> loadViewAction, string message = "Loading...")
	{
		_ = 1;
		try
		{
			BusyMessage = message;
			IsBusy = true;
			await Task.Yield();
			await loadViewAction();
		}
		catch (Exception exception)
		{
			_logger.LogError(exception, "Navigation load failed: {Message}", BusyMessage);
		}
		finally
		{
			IsBusy = false;
		}
	}

	private void UpdateClock()
	{
		LocalDateTime = DateTime.Now.ToString("f");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnFolderSyncWarningChanged(string value)
	{
		OnPropertyChanged("HasFolderSyncWarning");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnIsImportingCatalogueChanged(bool value)
	{
		OnPropertyChanged("HasStatusBanner");
	}
}
