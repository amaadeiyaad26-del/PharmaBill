using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class SettingsPageViewModel : SectionPageViewModel, ILoadablePage
{
	private readonly IThemeService _themeService;

	private readonly ILanguageService _languageService;

	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _currentSession;

	private readonly IUserSessionService _userSession;

	private readonly IConfirmationService _confirmationService;

	private readonly INavigationService _navigationService;

	private readonly IPaymentQrDialogService _paymentQrDialogs;

	private readonly GeminiApiKeyStore _geminiApiKeyStore;

	private readonly DocumentOutputSettingsStore _documentOutputSettingsStore;

	private readonly DataRetentionSettingsStore _retentionSettingsStore;

	private readonly RegistrationNotificationService _registrationNotifications;

	private readonly IUserManualDialogService _userManualDialogs;

	private readonly IAppUpdateService _appUpdates;

	private DocumentOutputSettings _documentOutputSettings;

	private readonly AppVersionInfo _versionInfo;

	private bool _suppressRetentionSave;

	private readonly IReadOnlyList<SettingsSearchEntry> _settingsIndex;

	[ObservableProperty]
	private OperatorAccountRow? _selectedOperator;

	[ObservableProperty]
	private string _newOperatorUserName = string.Empty;

	[ObservableProperty]
	private string _newOperatorDisplayName = string.Empty;

	[ObservableProperty]
	private string _newOperatorPassword = string.Empty;

	[ObservableProperty]
	private string _newOperatorRole = "Cashier";

	[ObservableProperty]
	private string _operatorStatus = string.Empty;

	[ObservableProperty]
	private string _branchCode = "BR01-MAIN";

	[ObservableProperty]
	private string _branchName = "Main branch";

	[ObservableProperty]
	private string _branchAddress = string.Empty;

	[ObservableProperty]
	private string _branchPhone = string.Empty;

	[ObservableProperty]
	private string _branchGstin = string.Empty;

	[ObservableProperty]
	private string _branchDrugLicense = string.Empty;

	[ObservableProperty]
	private bool _branchIsHeadOffice = true;

	[ObservableProperty]
	private string _branchStatus = string.Empty;

	[ObservableProperty]
	private RetentionPeriodChoice _selectedRetentionPeriod;

	[ObservableProperty]
	private string _activeRecordsSummary = "Active Records: —";

	[ObservableProperty]
	private string _retentionStatus = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor("CanArchive")]
	private bool _isArchiving;

	[ObservableProperty]
	private string _updateStatus = string.Empty;

	[ObservableProperty]
	private string _releaseNotesText = string.Empty;

	[ObservableProperty]
	private bool _isReleaseNotesVisible;

	[ObservableProperty]
	private bool _isCheckingForUpdates;

	private const string DefaultPrinterOption = "(Windows default)";

	[ObservableProperty]
	private string _geminiApiKeyInput = string.Empty;

	[ObservableProperty]
	private string _geminiApiKeyStatus = string.Empty;

	[ObservableProperty]
	private DocumentPaperSize _retailMemoPaperSize;

	[ObservableProperty]
	private DocumentPaperSize _retailInvoicePaperSize;

	[ObservableProperty]
	private string _retailMemoPrinter = "(Windows default)";

	[ObservableProperty]
	private string _retailInvoicePrinter = "(Windows default)";

	[ObservableProperty]
	private string _footerText = string.Empty;

	[ObservableProperty]
	private string _logoPath = string.Empty;

	[ObservableProperty]
	private string _whatsAppNumber = string.Empty;

	[ObservableProperty]
	private string _smtpHost = string.Empty;

	[ObservableProperty]
	private int _smtpPort = 587;

	[ObservableProperty]
	private string _smtpFromAddress = string.Empty;

	[ObservableProperty]
	private string _smtpUserName = string.Empty;

	[ObservableProperty]
	private string _smtpPassword = string.Empty;

	[ObservableProperty]
	private bool _smtpEnableSsl = true;

	[ObservableProperty]
	private bool _cashDrawerPulse;

	[ObservableProperty]
	private InvoiceTemplateType _defaultInvoiceTemplate = InvoiceTemplateType.AlwaysAsk;

	[ObservableProperty]
	private bool _silentPrint = true;

	[ObservableProperty]
	private bool _showCustomerQrWindow;

	[ObservableProperty]
	private bool _printDoctorName = true;

	[ObservableProperty]
	private bool _printCustomerPhone = true;

	[ObservableProperty]
	private bool _printShopLogo = true;

	[ObservableProperty]
	private bool _printTermsDisclaimer = true;

	[ObservableProperty]
	private string _pharmacyStoreName = string.Empty;

	[ObservableProperty]
	private string _pharmacyAddress = string.Empty;

	[ObservableProperty]
	private string _printAddressLine2 = string.Empty;

	[ObservableProperty]
	private string _pharmacyPhone = string.Empty;

	[ObservableProperty]
	private string _pharmacyGstin = string.Empty;

	[ObservableProperty]
	private string _fssaiNumber = string.Empty;

	[ObservableProperty]
	private string _drugLicence20B = string.Empty;

	[ObservableProperty]
	private string _drugLicence21B = string.Empty;

	[ObservableProperty]
	private string _termsAndDisclaimer = string.Empty;

	[ObservableProperty]
	private string _documentOutputStatus = string.Empty;

	[ObservableProperty]
	private string _pharmacyEmail = string.Empty;

	[ObservableProperty]
	private string _firmProfileStatus = string.Empty;

	[ObservableProperty]
	private string _manualEmailMissingMessage = string.Empty;

	[ObservableProperty]
	private string _manualHubStatus = string.Empty;

	[ObservableProperty]
	private string _firmEmailRevealToken = string.Empty;

	[ObservableProperty]
	private string _selectedSettingsCategory = "General";

	[ObservableProperty]
	private bool _isGeneralCategorySelected = true;

	[ObservableProperty]
	private bool _isPaymentsCategorySelected;

	[ObservableProperty]
	private bool _isPrinterCategorySelected;

	[ObservableProperty]
	private bool _isUsersCategorySelected;

	[ObservableProperty]
	private bool _isDataCategorySelected;

	[ObservableProperty]
	private bool _isAboutCategorySelected;

	[ObservableProperty]
	private bool _showInspectorSection = true;

	[ObservableProperty]
	private bool _highlightInspectorSection;

	[ObservableProperty]
	private string _storeUpiId = string.Empty;

	[ObservableProperty]
	private string _upiPayeeName = string.Empty;

	[ObservableProperty]
	private string _upiSettingsStatus = string.Empty;

	[ObservableProperty]
	private string _upiVpaError = string.Empty;

	[ObservableProperty]
	private string _upiCardRevealToken = string.Empty;

	[ObservableProperty]
	private string _settingsSearchQuery = string.Empty;

	[ObservableProperty]
	private string _settingsSearchFocusToken = string.Empty;

	[ObservableProperty]
	private bool _hasActiveSettingsSearch;

	[ObservableProperty]
	private bool _hasSettingsSearchResults = true;

	[ObservableProperty]
	private string _settingsSearchEmptyMessage = string.Empty;

	[ObservableProperty]
	private bool _showAppearanceSection = true;

	[ObservableProperty]
	private bool _highlightAppearanceSection;

	[ObservableProperty]
	private bool _showBusinessModeSection = true;

	[ObservableProperty]
	private bool _highlightBusinessModeSection;

	[ObservableProperty]
	private bool _showBranchSection = true;

	[ObservableProperty]
	private bool _highlightBranchSection;

	[ObservableProperty]
	private bool _showOperatorsSection = true;

	[ObservableProperty]
	private bool _highlightOperatorsSection;

	[ObservableProperty]
	private bool _showGeminiSection = true;

	[ObservableProperty]
	private bool _highlightGeminiSection;

	[ObservableProperty]
	private bool _showFirmProfileSection = true;

	[ObservableProperty]
	private bool _highlightFirmProfileSection;

	[ObservableProperty]
	private bool _showPaymentSection = true;

	[ObservableProperty]
	private bool _highlightPaymentSection;

	[ObservableProperty]
	private bool _showPrinterSection = true;

	[ObservableProperty]
	private bool _highlightPrinterSection;

	[ObservableProperty]
	private bool _showDocumentOutputSection = true;

	[ObservableProperty]
	private bool _highlightDocumentOutputSection;

	[ObservableProperty]
	private bool _showDataBackupSection = true;

	[ObservableProperty]
	private bool _highlightDataBackupSection;

	[ObservableProperty]
	private bool _showUserManualSection = true;

	[ObservableProperty]
	private bool _highlightUserManualSection;

	[ObservableProperty]
	private bool _showAboutSection = true;

	[ObservableProperty]
	private bool _highlightAboutSection;

	[ObservableProperty]
	private bool _showSyncShortcut;

	[ObservableProperty]
	private bool _highlightSyncShortcut;

	[ObservableProperty]
	private bool _showBackupShortcut;

	[ObservableProperty]
	private bool _highlightBackupShortcut;

	[ObservableProperty]
	private string _inspectorPinInput = string.Empty;

	[ObservableProperty]
	private string _inspectorPinConfirmation = string.Empty;

	[ObservableProperty]
	private BusinessMode _selectedBusinessMode;

	[ObservableProperty]
	private string _modeError = string.Empty;

	[ObservableProperty]
	private string _selectedTheme;

	[ObservableProperty]
	private string _selectedAccent;

	[ObservableProperty]
	private string _selectedLanguage;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveBranchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addOperatorCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? deactivateOperatorCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? reactivateOperatorCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? changeOperatorRoleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? resetOperatorPasswordCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? refreshRetentionSummaryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? archivePreRetentionRecordsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? checkForUpdatesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? toggleReleaseNotesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openInteractiveUserManualCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openPdfUserManualCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? emailUserManualAgainCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? pickLogoCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveDocumentOutputSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? clearSettingsSearchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? focusSettingsSearchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? selectSettingsCategoryCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveFirmProfileCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveUpiSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? previewUpiQrCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<string?>? openRelatedSettingsSectionCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? removeSmtpPasswordCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? saveGeminiApiKeyCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? removeGeminiApiKeyCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? changeBusinessModeCommand;

	public bool CanManageOperators => _userSession.CanManageUsers;

	public IReadOnlyList<string> OperatorRoleOptions { get; }

	public ObservableCollection<OperatorAccountRow> Operators { get; } = new ObservableCollection<OperatorAccountRow>();

	public IReadOnlyList<RetentionPeriodChoice> RetentionPeriodOptions { get; }

	public bool CanArchive => !IsArchiving;

	public string VersionText => _versionInfo.Display;

	public IReadOnlyList<string> ThemeOptions { get; }

	public IReadOnlyList<string> AccentOptions { get; }

	public IReadOnlyList<string> LanguageOptions { get; }

	public IReadOnlyList<BusinessMode> BusinessModes { get; }

	public IReadOnlyList<DocumentPaperSize> PaperSizeOptions { get; }

	public IReadOnlyList<InvoiceTemplateType> InvoiceTemplateOptions { get; }

	public ObservableCollection<string> PrinterOptions { get; }

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public OperatorAccountRow? SelectedOperator
	{
		get
		{
			return _selectedOperator;
		}
		set
		{
			if (!EqualityComparer<OperatorAccountRow>.Default.Equals(_selectedOperator, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedOperator);
				_selectedOperator = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedOperator);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewOperatorUserName
	{
		get
		{
			return _newOperatorUserName;
		}
		[MemberNotNull("_newOperatorUserName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newOperatorUserName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NewOperatorUserName);
				_newOperatorUserName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NewOperatorUserName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewOperatorDisplayName
	{
		get
		{
			return _newOperatorDisplayName;
		}
		[MemberNotNull("_newOperatorDisplayName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newOperatorDisplayName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NewOperatorDisplayName);
				_newOperatorDisplayName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NewOperatorDisplayName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewOperatorPassword
	{
		get
		{
			return _newOperatorPassword;
		}
		[MemberNotNull("_newOperatorPassword")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newOperatorPassword, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NewOperatorPassword);
				_newOperatorPassword = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NewOperatorPassword);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewOperatorRole
	{
		get
		{
			return _newOperatorRole;
		}
		[MemberNotNull("_newOperatorRole")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newOperatorRole, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NewOperatorRole);
				_newOperatorRole = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NewOperatorRole);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string OperatorStatus
	{
		get
		{
			return _operatorStatus;
		}
		[MemberNotNull("_operatorStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_operatorStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.OperatorStatus);
				_operatorStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.OperatorStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BranchCode
	{
		get
		{
			return _branchCode;
		}
		[MemberNotNull("_branchCode")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_branchCode, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchCode);
				_branchCode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchCode);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BranchName
	{
		get
		{
			return _branchName;
		}
		[MemberNotNull("_branchName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_branchName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchName);
				_branchName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BranchAddress
	{
		get
		{
			return _branchAddress;
		}
		[MemberNotNull("_branchAddress")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_branchAddress, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchAddress);
				_branchAddress = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchAddress);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BranchPhone
	{
		get
		{
			return _branchPhone;
		}
		[MemberNotNull("_branchPhone")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_branchPhone, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchPhone);
				_branchPhone = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchPhone);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BranchGstin
	{
		get
		{
			return _branchGstin;
		}
		[MemberNotNull("_branchGstin")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_branchGstin, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchGstin);
				_branchGstin = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchGstin);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BranchDrugLicense
	{
		get
		{
			return _branchDrugLicense;
		}
		[MemberNotNull("_branchDrugLicense")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_branchDrugLicense, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchDrugLicense);
				_branchDrugLicense = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchDrugLicense);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool BranchIsHeadOffice
	{
		get
		{
			return _branchIsHeadOffice;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_branchIsHeadOffice, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchIsHeadOffice);
				_branchIsHeadOffice = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchIsHeadOffice);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BranchStatus
	{
		get
		{
			return _branchStatus;
		}
		[MemberNotNull("_branchStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_branchStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BranchStatus);
				_branchStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BranchStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RetentionPeriodChoice SelectedRetentionPeriod
	{
		get
		{
			return _selectedRetentionPeriod;
		}
		[MemberNotNull("_selectedRetentionPeriod")]
		set
		{
			if (!EqualityComparer<RetentionPeriodChoice>.Default.Equals(_selectedRetentionPeriod, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedRetentionPeriod);
				_selectedRetentionPeriod = value;
				OnSelectedRetentionPeriodChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedRetentionPeriod);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ActiveRecordsSummary
	{
		get
		{
			return _activeRecordsSummary;
		}
		[MemberNotNull("_activeRecordsSummary")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_activeRecordsSummary, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ActiveRecordsSummary);
				_activeRecordsSummary = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ActiveRecordsSummary);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string RetentionStatus
	{
		get
		{
			return _retentionStatus;
		}
		[MemberNotNull("_retentionStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_retentionStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RetentionStatus);
				_retentionStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RetentionStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsArchiving
	{
		get
		{
			return _isArchiving;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isArchiving, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsArchiving);
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CanArchive);
				_isArchiving = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsArchiving);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CanArchive);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpdateStatus
	{
		get
		{
			return _updateStatus;
		}
		[MemberNotNull("_updateStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_updateStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UpdateStatus);
				_updateStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UpdateStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ReleaseNotesText
	{
		get
		{
			return _releaseNotesText;
		}
		[MemberNotNull("_releaseNotesText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_releaseNotesText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ReleaseNotesText);
				_releaseNotesText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ReleaseNotesText);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsReleaseNotesVisible
	{
		get
		{
			return _isReleaseNotesVisible;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isReleaseNotesVisible, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsReleaseNotesVisible);
				_isReleaseNotesVisible = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsReleaseNotesVisible);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsCheckingForUpdates
	{
		get
		{
			return _isCheckingForUpdates;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isCheckingForUpdates, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsCheckingForUpdates);
				_isCheckingForUpdates = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsCheckingForUpdates);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GeminiApiKeyInput
	{
		get
		{
			return _geminiApiKeyInput;
		}
		[MemberNotNull("_geminiApiKeyInput")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_geminiApiKeyInput, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GeminiApiKeyInput);
				_geminiApiKeyInput = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GeminiApiKeyInput);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string GeminiApiKeyStatus
	{
		get
		{
			return _geminiApiKeyStatus;
		}
		[MemberNotNull("_geminiApiKeyStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_geminiApiKeyStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.GeminiApiKeyStatus);
				_geminiApiKeyStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.GeminiApiKeyStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DocumentPaperSize RetailMemoPaperSize
	{
		get
		{
			return _retailMemoPaperSize;
		}
		set
		{
			if (!EqualityComparer<DocumentPaperSize>.Default.Equals(_retailMemoPaperSize, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RetailMemoPaperSize);
				_retailMemoPaperSize = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RetailMemoPaperSize);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DocumentPaperSize RetailInvoicePaperSize
	{
		get
		{
			return _retailInvoicePaperSize;
		}
		set
		{
			if (!EqualityComparer<DocumentPaperSize>.Default.Equals(_retailInvoicePaperSize, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RetailInvoicePaperSize);
				_retailInvoicePaperSize = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RetailInvoicePaperSize);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string RetailMemoPrinter
	{
		get
		{
			return _retailMemoPrinter;
		}
		[MemberNotNull("_retailMemoPrinter")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_retailMemoPrinter, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RetailMemoPrinter);
				_retailMemoPrinter = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RetailMemoPrinter);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string RetailInvoicePrinter
	{
		get
		{
			return _retailInvoicePrinter;
		}
		[MemberNotNull("_retailInvoicePrinter")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_retailInvoicePrinter, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.RetailInvoicePrinter);
				_retailInvoicePrinter = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.RetailInvoicePrinter);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FooterText
	{
		get
		{
			return _footerText;
		}
		[MemberNotNull("_footerText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_footerText, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FooterText);
				_footerText = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FooterText);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LogoPath
	{
		get
		{
			return _logoPath;
		}
		[MemberNotNull("_logoPath")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_logoPath, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LogoPath);
				_logoPath = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LogoPath);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string WhatsAppNumber
	{
		get
		{
			return _whatsAppNumber;
		}
		[MemberNotNull("_whatsAppNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_whatsAppNumber, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.WhatsAppNumber);
				_whatsAppNumber = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.WhatsAppNumber);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SmtpHost
	{
		get
		{
			return _smtpHost;
		}
		[MemberNotNull("_smtpHost")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_smtpHost, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SmtpHost);
				_smtpHost = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SmtpHost);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public int SmtpPort
	{
		get
		{
			return _smtpPort;
		}
		set
		{
			if (!EqualityComparer<int>.Default.Equals(_smtpPort, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SmtpPort);
				_smtpPort = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SmtpPort);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SmtpFromAddress
	{
		get
		{
			return _smtpFromAddress;
		}
		[MemberNotNull("_smtpFromAddress")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_smtpFromAddress, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SmtpFromAddress);
				_smtpFromAddress = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SmtpFromAddress);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SmtpUserName
	{
		get
		{
			return _smtpUserName;
		}
		[MemberNotNull("_smtpUserName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_smtpUserName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SmtpUserName);
				_smtpUserName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SmtpUserName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SmtpPassword
	{
		get
		{
			return _smtpPassword;
		}
		[MemberNotNull("_smtpPassword")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_smtpPassword, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SmtpPassword);
				_smtpPassword = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SmtpPassword);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool SmtpEnableSsl
	{
		get
		{
			return _smtpEnableSsl;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_smtpEnableSsl, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SmtpEnableSsl);
				_smtpEnableSsl = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SmtpEnableSsl);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CashDrawerPulse
	{
		get
		{
			return _cashDrawerPulse;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_cashDrawerPulse, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CashDrawerPulse);
				_cashDrawerPulse = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CashDrawerPulse);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public InvoiceTemplateType DefaultInvoiceTemplate
	{
		get
		{
			return _defaultInvoiceTemplate;
		}
		set
		{
			if (!EqualityComparer<InvoiceTemplateType>.Default.Equals(_defaultInvoiceTemplate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DefaultInvoiceTemplate);
				_defaultInvoiceTemplate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DefaultInvoiceTemplate);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool SilentPrint
	{
		get
		{
			return _silentPrint;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_silentPrint, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SilentPrint);
				_silentPrint = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SilentPrint);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowCustomerQrWindow
	{
		get
		{
			return _showCustomerQrWindow;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showCustomerQrWindow, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowCustomerQrWindow);
				_showCustomerQrWindow = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowCustomerQrWindow);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool PrintDoctorName
	{
		get
		{
			return _printDoctorName;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_printDoctorName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PrintDoctorName);
				_printDoctorName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PrintDoctorName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool PrintCustomerPhone
	{
		get
		{
			return _printCustomerPhone;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_printCustomerPhone, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PrintCustomerPhone);
				_printCustomerPhone = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PrintCustomerPhone);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool PrintShopLogo
	{
		get
		{
			return _printShopLogo;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_printShopLogo, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PrintShopLogo);
				_printShopLogo = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PrintShopLogo);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool PrintTermsDisclaimer
	{
		get
		{
			return _printTermsDisclaimer;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_printTermsDisclaimer, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PrintTermsDisclaimer);
				_printTermsDisclaimer = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PrintTermsDisclaimer);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PharmacyStoreName
	{
		get
		{
			return _pharmacyStoreName;
		}
		[MemberNotNull("_pharmacyStoreName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pharmacyStoreName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PharmacyStoreName);
				_pharmacyStoreName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PharmacyStoreName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PharmacyAddress
	{
		get
		{
			return _pharmacyAddress;
		}
		[MemberNotNull("_pharmacyAddress")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pharmacyAddress, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PharmacyAddress);
				_pharmacyAddress = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PharmacyAddress);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PrintAddressLine2
	{
		get
		{
			return _printAddressLine2;
		}
		[MemberNotNull("_printAddressLine2")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_printAddressLine2, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PrintAddressLine2);
				_printAddressLine2 = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PrintAddressLine2);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PharmacyPhone
	{
		get
		{
			return _pharmacyPhone;
		}
		[MemberNotNull("_pharmacyPhone")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pharmacyPhone, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PharmacyPhone);
				_pharmacyPhone = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PharmacyPhone);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PharmacyGstin
	{
		get
		{
			return _pharmacyGstin;
		}
		[MemberNotNull("_pharmacyGstin")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pharmacyGstin, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PharmacyGstin);
				_pharmacyGstin = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PharmacyGstin);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FssaiNumber
	{
		get
		{
			return _fssaiNumber;
		}
		[MemberNotNull("_fssaiNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_fssaiNumber, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FssaiNumber);
				_fssaiNumber = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FssaiNumber);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DrugLicence20B
	{
		get
		{
			return _drugLicence20B;
		}
		[MemberNotNull("_drugLicence20B")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_drugLicence20B, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DrugLicence20B);
				_drugLicence20B = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DrugLicence20B);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DrugLicence21B
	{
		get
		{
			return _drugLicence21B;
		}
		[MemberNotNull("_drugLicence21B")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_drugLicence21B, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DrugLicence21B);
				_drugLicence21B = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DrugLicence21B);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string TermsAndDisclaimer
	{
		get
		{
			return _termsAndDisclaimer;
		}
		[MemberNotNull("_termsAndDisclaimer")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_termsAndDisclaimer, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.TermsAndDisclaimer);
				_termsAndDisclaimer = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.TermsAndDisclaimer);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DocumentOutputStatus
	{
		get
		{
			return _documentOutputStatus;
		}
		[MemberNotNull("_documentOutputStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_documentOutputStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DocumentOutputStatus);
				_documentOutputStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DocumentOutputStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PharmacyEmail
	{
		get
		{
			return _pharmacyEmail;
		}
		[MemberNotNull("_pharmacyEmail")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pharmacyEmail, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PharmacyEmail);
				_pharmacyEmail = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PharmacyEmail);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FirmProfileStatus
	{
		get
		{
			return _firmProfileStatus;
		}
		[MemberNotNull("_firmProfileStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_firmProfileStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FirmProfileStatus);
				_firmProfileStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FirmProfileStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ManualEmailMissingMessage
	{
		get
		{
			return _manualEmailMissingMessage;
		}
		[MemberNotNull("_manualEmailMissingMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_manualEmailMissingMessage, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ManualEmailMissingMessage);
				_manualEmailMissingMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ManualEmailMissingMessage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ManualHubStatus
	{
		get
		{
			return _manualHubStatus;
		}
		[MemberNotNull("_manualHubStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_manualHubStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ManualHubStatus);
				_manualHubStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ManualHubStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FirmEmailRevealToken
	{
		get
		{
			return _firmEmailRevealToken;
		}
		[MemberNotNull("_firmEmailRevealToken")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_firmEmailRevealToken, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.FirmEmailRevealToken);
				_firmEmailRevealToken = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.FirmEmailRevealToken);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedSettingsCategory
	{
		get
		{
			return _selectedSettingsCategory;
		}
		[MemberNotNull("_selectedSettingsCategory")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedSettingsCategory, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedSettingsCategory);
				_selectedSettingsCategory = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedSettingsCategory);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsGeneralCategorySelected
	{
		get
		{
			return _isGeneralCategorySelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isGeneralCategorySelected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsGeneralCategorySelected);
				_isGeneralCategorySelected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsGeneralCategorySelected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsPaymentsCategorySelected
	{
		get
		{
			return _isPaymentsCategorySelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isPaymentsCategorySelected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsPaymentsCategorySelected);
				_isPaymentsCategorySelected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsPaymentsCategorySelected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsPrinterCategorySelected
	{
		get
		{
			return _isPrinterCategorySelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isPrinterCategorySelected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsPrinterCategorySelected);
				_isPrinterCategorySelected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsPrinterCategorySelected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsUsersCategorySelected
	{
		get
		{
			return _isUsersCategorySelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isUsersCategorySelected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsUsersCategorySelected);
				_isUsersCategorySelected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsUsersCategorySelected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsDataCategorySelected
	{
		get
		{
			return _isDataCategorySelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isDataCategorySelected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsDataCategorySelected);
				_isDataCategorySelected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsDataCategorySelected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsAboutCategorySelected
	{
		get
		{
			return _isAboutCategorySelected;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isAboutCategorySelected, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsAboutCategorySelected);
				_isAboutCategorySelected = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsAboutCategorySelected);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowInspectorSection
	{
		get
		{
			return _showInspectorSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showInspectorSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowInspectorSection);
				_showInspectorSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowInspectorSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightInspectorSection
	{
		get
		{
			return _highlightInspectorSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightInspectorSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightInspectorSection);
				_highlightInspectorSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightInspectorSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string StoreUpiId
	{
		get
		{
			return _storeUpiId;
		}
		[MemberNotNull("_storeUpiId")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_storeUpiId, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StoreUpiId);
				_storeUpiId = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StoreUpiId);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpiPayeeName
	{
		get
		{
			return _upiPayeeName;
		}
		[MemberNotNull("_upiPayeeName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_upiPayeeName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UpiPayeeName);
				_upiPayeeName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UpiPayeeName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpiSettingsStatus
	{
		get
		{
			return _upiSettingsStatus;
		}
		[MemberNotNull("_upiSettingsStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_upiSettingsStatus, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UpiSettingsStatus);
				_upiSettingsStatus = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UpiSettingsStatus);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpiVpaError
	{
		get
		{
			return _upiVpaError;
		}
		[MemberNotNull("_upiVpaError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_upiVpaError, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UpiVpaError);
				_upiVpaError = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UpiVpaError);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpiCardRevealToken
	{
		get
		{
			return _upiCardRevealToken;
		}
		[MemberNotNull("_upiCardRevealToken")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_upiCardRevealToken, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UpiCardRevealToken);
				_upiCardRevealToken = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UpiCardRevealToken);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SettingsSearchQuery
	{
		get
		{
			return _settingsSearchQuery;
		}
		[MemberNotNull("_settingsSearchQuery")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_settingsSearchQuery, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SettingsSearchQuery);
				_settingsSearchQuery = value;
				OnSettingsSearchQueryChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SettingsSearchQuery);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SettingsSearchFocusToken
	{
		get
		{
			return _settingsSearchFocusToken;
		}
		[MemberNotNull("_settingsSearchFocusToken")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_settingsSearchFocusToken, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SettingsSearchFocusToken);
				_settingsSearchFocusToken = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SettingsSearchFocusToken);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasActiveSettingsSearch
	{
		get
		{
			return _hasActiveSettingsSearch;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasActiveSettingsSearch, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasActiveSettingsSearch);
				_hasActiveSettingsSearch = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasActiveSettingsSearch);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HasSettingsSearchResults
	{
		get
		{
			return _hasSettingsSearchResults;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_hasSettingsSearchResults, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HasSettingsSearchResults);
				_hasSettingsSearchResults = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HasSettingsSearchResults);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SettingsSearchEmptyMessage
	{
		get
		{
			return _settingsSearchEmptyMessage;
		}
		[MemberNotNull("_settingsSearchEmptyMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_settingsSearchEmptyMessage, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SettingsSearchEmptyMessage);
				_settingsSearchEmptyMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SettingsSearchEmptyMessage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowAppearanceSection
	{
		get
		{
			return _showAppearanceSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showAppearanceSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowAppearanceSection);
				_showAppearanceSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowAppearanceSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightAppearanceSection
	{
		get
		{
			return _highlightAppearanceSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightAppearanceSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightAppearanceSection);
				_highlightAppearanceSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightAppearanceSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowBusinessModeSection
	{
		get
		{
			return _showBusinessModeSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showBusinessModeSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowBusinessModeSection);
				_showBusinessModeSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowBusinessModeSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightBusinessModeSection
	{
		get
		{
			return _highlightBusinessModeSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightBusinessModeSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightBusinessModeSection);
				_highlightBusinessModeSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightBusinessModeSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowBranchSection
	{
		get
		{
			return _showBranchSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showBranchSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowBranchSection);
				_showBranchSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowBranchSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightBranchSection
	{
		get
		{
			return _highlightBranchSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightBranchSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightBranchSection);
				_highlightBranchSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightBranchSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowOperatorsSection
	{
		get
		{
			return _showOperatorsSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showOperatorsSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowOperatorsSection);
				_showOperatorsSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowOperatorsSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightOperatorsSection
	{
		get
		{
			return _highlightOperatorsSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightOperatorsSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightOperatorsSection);
				_highlightOperatorsSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightOperatorsSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowGeminiSection
	{
		get
		{
			return _showGeminiSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showGeminiSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowGeminiSection);
				_showGeminiSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowGeminiSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightGeminiSection
	{
		get
		{
			return _highlightGeminiSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightGeminiSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightGeminiSection);
				_highlightGeminiSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightGeminiSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowFirmProfileSection
	{
		get
		{
			return _showFirmProfileSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showFirmProfileSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowFirmProfileSection);
				_showFirmProfileSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowFirmProfileSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightFirmProfileSection
	{
		get
		{
			return _highlightFirmProfileSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightFirmProfileSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightFirmProfileSection);
				_highlightFirmProfileSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightFirmProfileSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowPaymentSection
	{
		get
		{
			return _showPaymentSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showPaymentSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowPaymentSection);
				_showPaymentSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowPaymentSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightPaymentSection
	{
		get
		{
			return _highlightPaymentSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightPaymentSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightPaymentSection);
				_highlightPaymentSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightPaymentSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowPrinterSection
	{
		get
		{
			return _showPrinterSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showPrinterSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowPrinterSection);
				_showPrinterSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowPrinterSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightPrinterSection
	{
		get
		{
			return _highlightPrinterSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightPrinterSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightPrinterSection);
				_highlightPrinterSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightPrinterSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowDocumentOutputSection
	{
		get
		{
			return _showDocumentOutputSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showDocumentOutputSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowDocumentOutputSection);
				_showDocumentOutputSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowDocumentOutputSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightDocumentOutputSection
	{
		get
		{
			return _highlightDocumentOutputSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightDocumentOutputSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightDocumentOutputSection);
				_highlightDocumentOutputSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightDocumentOutputSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowDataBackupSection
	{
		get
		{
			return _showDataBackupSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showDataBackupSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowDataBackupSection);
				_showDataBackupSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowDataBackupSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightDataBackupSection
	{
		get
		{
			return _highlightDataBackupSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightDataBackupSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightDataBackupSection);
				_highlightDataBackupSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightDataBackupSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowUserManualSection
	{
		get
		{
			return _showUserManualSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showUserManualSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowUserManualSection);
				_showUserManualSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowUserManualSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightUserManualSection
	{
		get
		{
			return _highlightUserManualSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightUserManualSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightUserManualSection);
				_highlightUserManualSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightUserManualSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowAboutSection
	{
		get
		{
			return _showAboutSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showAboutSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowAboutSection);
				_showAboutSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowAboutSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightAboutSection
	{
		get
		{
			return _highlightAboutSection;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightAboutSection, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightAboutSection);
				_highlightAboutSection = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightAboutSection);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowSyncShortcut
	{
		get
		{
			return _showSyncShortcut;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showSyncShortcut, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowSyncShortcut);
				_showSyncShortcut = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowSyncShortcut);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightSyncShortcut
	{
		get
		{
			return _highlightSyncShortcut;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightSyncShortcut, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightSyncShortcut);
				_highlightSyncShortcut = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightSyncShortcut);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowBackupShortcut
	{
		get
		{
			return _showBackupShortcut;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showBackupShortcut, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ShowBackupShortcut);
				_showBackupShortcut = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ShowBackupShortcut);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool HighlightBackupShortcut
	{
		get
		{
			return _highlightBackupShortcut;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_highlightBackupShortcut, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.HighlightBackupShortcut);
				_highlightBackupShortcut = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.HighlightBackupShortcut);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string InspectorPinInput
	{
		get
		{
			return _inspectorPinInput;
		}
		[MemberNotNull("_inspectorPinInput")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_inspectorPinInput, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.InspectorPinInput);
				_inspectorPinInput = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.InspectorPinInput);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string InspectorPinConfirmation
	{
		get
		{
			return _inspectorPinConfirmation;
		}
		[MemberNotNull("_inspectorPinConfirmation")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_inspectorPinConfirmation, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.InspectorPinConfirmation);
				_inspectorPinConfirmation = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.InspectorPinConfirmation);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public BusinessMode SelectedBusinessMode
	{
		get
		{
			return _selectedBusinessMode;
		}
		set
		{
			if (!EqualityComparer<BusinessMode>.Default.Equals(_selectedBusinessMode, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedBusinessMode);
				_selectedBusinessMode = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedBusinessMode);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ModeError
	{
		get
		{
			return _modeError;
		}
		[MemberNotNull("_modeError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_modeError, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ModeError);
				_modeError = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ModeError);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedTheme
	{
		get
		{
			return _selectedTheme;
		}
		[MemberNotNull("_selectedTheme")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedTheme, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedTheme);
				_selectedTheme = value;
				OnSelectedThemeChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedTheme);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedAccent
	{
		get
		{
			return _selectedAccent;
		}
		[MemberNotNull("_selectedAccent")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedAccent, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedAccent);
				_selectedAccent = value;
				OnSelectedAccentChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedAccent);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SelectedLanguage
	{
		get
		{
			return _selectedLanguage;
		}
		[MemberNotNull("_selectedLanguage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_selectedLanguage, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedLanguage);
				_selectedLanguage = value;
				OnSelectedLanguageChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedLanguage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveBranchCommand => saveBranchCommand ?? (saveBranchCommand = new AsyncRelayCommand(SaveBranchAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddOperatorCommand => addOperatorCommand ?? (addOperatorCommand = new AsyncRelayCommand(AddOperatorAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand DeactivateOperatorCommand => deactivateOperatorCommand ?? (deactivateOperatorCommand = new AsyncRelayCommand(DeactivateOperatorAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ReactivateOperatorCommand => reactivateOperatorCommand ?? (reactivateOperatorCommand = new AsyncRelayCommand(ReactivateOperatorAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ChangeOperatorRoleCommand => changeOperatorRoleCommand ?? (changeOperatorRoleCommand = new AsyncRelayCommand(ChangeOperatorRoleAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ResetOperatorPasswordCommand => resetOperatorPasswordCommand ?? (resetOperatorPasswordCommand = new AsyncRelayCommand(ResetOperatorPasswordAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RefreshRetentionSummaryCommand => refreshRetentionSummaryCommand ?? (refreshRetentionSummaryCommand = new AsyncRelayCommand(RefreshRetentionSummaryAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ArchivePreRetentionRecordsCommand => archivePreRetentionRecordsCommand ?? (archivePreRetentionRecordsCommand = new AsyncRelayCommand(ArchivePreRetentionRecordsAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CheckForUpdatesCommand => checkForUpdatesCommand ?? (checkForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ToggleReleaseNotesCommand => toggleReleaseNotesCommand ?? (toggleReleaseNotesCommand = new RelayCommand(ToggleReleaseNotes));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenInteractiveUserManualCommand => openInteractiveUserManualCommand ?? (openInteractiveUserManualCommand = new RelayCommand(OpenInteractiveUserManual));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenPdfUserManualCommand => openPdfUserManualCommand ?? (openPdfUserManualCommand = new RelayCommand(OpenPdfUserManual));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand EmailUserManualAgainCommand => emailUserManualAgainCommand ?? (emailUserManualAgainCommand = new AsyncRelayCommand(EmailUserManualAgainAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PickLogoCommand => pickLogoCommand ?? (pickLogoCommand = new RelayCommand(PickLogo));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveDocumentOutputSettingsCommand => saveDocumentOutputSettingsCommand ?? (saveDocumentOutputSettingsCommand = new AsyncRelayCommand(SaveDocumentOutputSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ClearSettingsSearchCommand => clearSettingsSearchCommand ?? (clearSettingsSearchCommand = new RelayCommand(ClearSettingsSearch));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand FocusSettingsSearchCommand => focusSettingsSearchCommand ?? (focusSettingsSearchCommand = new RelayCommand(FocusSettingsSearch));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> SelectSettingsCategoryCommand => selectSettingsCategoryCommand ?? (selectSettingsCategoryCommand = new RelayCommand<string>(SelectSettingsCategory));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveFirmProfileCommand => saveFirmProfileCommand ?? (saveFirmProfileCommand = new AsyncRelayCommand(SaveFirmProfileAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveUpiSettingsCommand => saveUpiSettingsCommand ?? (saveUpiSettingsCommand = new AsyncRelayCommand(SaveUpiSettingsAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PreviewUpiQrCommand => previewUpiQrCommand ?? (previewUpiQrCommand = new RelayCommand(PreviewUpiQr));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<string?> OpenRelatedSettingsSectionCommand => openRelatedSettingsSectionCommand ?? (openRelatedSettingsSectionCommand = new RelayCommand<string>(OpenRelatedSettingsSection));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand RemoveSmtpPasswordCommand => removeSmtpPasswordCommand ?? (removeSmtpPasswordCommand = new RelayCommand(RemoveSmtpPassword));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand SaveGeminiApiKeyCommand => saveGeminiApiKeyCommand ?? (saveGeminiApiKeyCommand = new RelayCommand(SaveGeminiApiKey));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand RemoveGeminiApiKeyCommand => removeGeminiApiKeyCommand ?? (removeGeminiApiKeyCommand = new RelayCommand(RemoveGeminiApiKey));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ChangeBusinessModeCommand => changeBusinessModeCommand ?? (changeBusinessModeCommand = new AsyncRelayCommand(ChangeBusinessModeAsync));

	public event EventHandler? BusinessModeChanged;

	public SettingsPageViewModel(IThemeService themeService, ILanguageService languageService, IServiceScopeFactory scopeFactory, CurrentSession currentSession, IUserSessionService userSession, IConfirmationService confirmationService, INavigationService navigationService, IPaymentQrDialogService paymentQrDialogs, GeminiApiKeyStore geminiApiKeyStore, DocumentOutputSettingsStore documentOutputSettingsStore, DataRetentionSettingsStore retentionSettingsStore, RegistrationNotificationService registrationNotifications, IUserManualDialogService userManualDialogs, IAppUpdateService appUpdates, AppVersionInfo versionInfo)
		: base(languageService.GetString("NavSettings"))
	{
		_themeService = themeService;
		_languageService = languageService;
		_scopeFactory = scopeFactory;
		_currentSession = currentSession;
		_userSession = userSession;
		_confirmationService = confirmationService;
		_navigationService = navigationService;
		_paymentQrDialogs = paymentQrDialogs;
		_geminiApiKeyStore = geminiApiKeyStore;
		_documentOutputSettingsStore = documentOutputSettingsStore;
		_retentionSettingsStore = retentionSettingsStore;
		_registrationNotifications = registrationNotifications;
		_userManualDialogs = userManualDialogs;
		_appUpdates = appUpdates;
		_versionInfo = versionInfo;
		_settingsIndex = BuildSettingsIndex();
		ApplySettingsSearchFilter(string.Empty);
		_documentOutputSettings = documentOutputSettingsStore.Load();
		ThemeOptions = themeService.ThemeOptions;
		AccentOptions = themeService.AccentOptions;
		LanguageOptions = languageService.LanguageOptions;
		BusinessModes = Enum.GetValues<BusinessMode>();
		OperatorRoleOptions = PermissionMatrix.OperatorRoleLabels;
		RetentionPeriodOptions = new _003C_003Ez__ReadOnlyArray<RetentionPeriodChoice>(new RetentionPeriodChoice[3]
		{
			new RetentionPeriodChoice(DataRetentionPeriod.FiveYears, StatutoryRetentionPolicy.Describe(DataRetentionPeriod.FiveYears)),
			new RetentionPeriodChoice(DataRetentionPeriod.SevenYears, StatutoryRetentionPolicy.Describe(DataRetentionPeriod.SevenYears)),
			new RetentionPeriodChoice(DataRetentionPeriod.Permanent, StatutoryRetentionPolicy.Describe(DataRetentionPeriod.Permanent))
		});
		SelectedTheme = "Light";
		SelectedAccent = "Blue";
		SelectedLanguage = "English";
		_languageService.LanguageChanged += OnLanguageChanged;
		PaperSizeOptions = Enum.GetValues<DocumentPaperSize>();
		InvoiceTemplateOptions = Enum.GetValues<InvoiceTemplateType>();
		PrinterOptions = LoadPrinters();
		RetailMemoPaperSize = _documentOutputSettings.RetailMemoPaperSize;
		RetailInvoicePaperSize = _documentOutputSettings.RetailInvoicePaperSize;
		RetailMemoPrinter = _documentOutputSettings.RetailMemoPrinter ?? "(Windows default)";
		RetailInvoicePrinter = _documentOutputSettings.RetailInvoicePrinter ?? "(Windows default)";
		FooterText = _documentOutputSettings.FooterText;
		LogoPath = _documentOutputSettings.LogoPath ?? string.Empty;
		WhatsAppNumber = _documentOutputSettings.WhatsAppNumber;
		SmtpHost = _documentOutputSettings.SmtpHost;
		SmtpPort = _documentOutputSettings.SmtpPort;
		SmtpFromAddress = _documentOutputSettings.SmtpFromAddress;
		SmtpUserName = _documentOutputSettings.SmtpUserName;
		SmtpEnableSsl = _documentOutputSettings.SmtpEnableSsl;
		CashDrawerPulse = _documentOutputSettings.CashDrawerPulse;
		DefaultInvoiceTemplate = _documentOutputSettings.DefaultInvoiceTemplate;
		SilentPrint = _documentOutputSettings.SilentPrint;
		ShowCustomerQrWindow = _documentOutputSettings.ShowCustomerQrWindow;
		PrintDoctorName = _documentOutputSettings.PrintDoctorName;
		PrintCustomerPhone = _documentOutputSettings.PrintCustomerPhone;
		PrintShopLogo = _documentOutputSettings.PrintShopLogo;
		PrintTermsDisclaimer = _documentOutputSettings.PrintTermsDisclaimer;
		PrintAddressLine2 = _documentOutputSettings.AddressLine2;
		FssaiNumber = _documentOutputSettings.FssaiNumber;
		DrugLicence20B = _documentOutputSettings.DrugLicence20B;
		DrugLicence21B = _documentOutputSettings.DrugLicence21B;
		TermsAndDisclaimer = _documentOutputSettings.TermsAndDisclaimer;
		_suppressRetentionSave = true;
		SelectedRetentionPeriod = RetentionPeriodOptions.First((RetentionPeriodChoice option) => option.Period == _retentionSettingsStore.Load().Period);
		_suppressRetentionSave = false;
		NewOperatorRole = "Cashier";
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await RefreshRetentionSummaryAsync(cancellationToken);
		await LoadPharmacyBrandingAsync(cancellationToken);
		await LoadOperatorsAsync(cancellationToken);
		await LoadBranchAsync(cancellationToken);
		if (!HasActiveSettingsSearch)
		{
			ApplyCategoryVisibility(SelectedSettingsCategory);
		}
		RefreshCategorySelectionFlags();
	}

	private async Task LoadBranchAsync(CancellationToken cancellationToken)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			Branch branch = await scope.ServiceProvider.GetRequiredService<BranchService>().EnsureCurrentBranchAsync(cancellationToken);
			BranchCode = branch.Code;
			BranchName = branch.BranchName;
			BranchAddress = branch.Address ?? string.Empty;
			BranchPhone = branch.ContactPhone ?? string.Empty;
			BranchGstin = branch.Gstin ?? string.Empty;
			BranchDrugLicense = branch.DrugLicenseNo ?? string.Empty;
			BranchIsHeadOffice = branch.IsHeadOffice;
			BranchStatus = "Active branch " + branch.Code + " · invoice series " + branch.InvoicePrefix;
		}
		catch (Exception ex)
		{
			BranchStatus = ex.Message;
		}
	}

	[RelayCommand]
	private async Task SaveBranchAsync()
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<BranchService>().UpdateCurrentBranchAsync(BranchCode, BranchName, BranchAddress, BranchPhone, BranchGstin, BranchDrugLicense, BranchIsHeadOffice);
			BranchStatus = "Saved successfully ✓";
		}
		catch (Exception ex)
		{
			BranchStatus = ex.Message;
		}
	}

	private async Task LoadOperatorsAsync(CancellationToken cancellationToken)
	{
		if (!CanManageOperators || _currentSession.User == null)
		{
			Operators.Clear();
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			IReadOnlyList<OperatorAccountRow> readOnlyList = await scope.ServiceProvider.GetRequiredService<OperatorAccountService>().ListAsync(cancellationToken);
			Operators.Clear();
			foreach (OperatorAccountRow item in readOnlyList)
			{
				Operators.Add(item);
			}
			OperatorStatus = $"{Operators.Count} operator account(s).";
		}
		catch (Exception ex)
		{
			OperatorStatus = ex.Message;
		}
	}

	[RelayCommand]
	private async Task AddOperatorAsync()
	{
		if (_currentSession.User == null || !CanManageOperators)
		{
			OperatorStatus = "Only an Admin can manage operators.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<OperatorAccountService>().CreateAsync(NewOperatorUserName, string.IsNullOrWhiteSpace(NewOperatorDisplayName) ? NewOperatorUserName : NewOperatorDisplayName, NewOperatorPassword, NewOperatorRole, _currentSession.User.Id, _currentSession.User.Role);
			NewOperatorUserName = string.Empty;
			NewOperatorDisplayName = string.Empty;
			NewOperatorPassword = string.Empty;
			OperatorStatus = "Operator created.";
			await LoadOperatorsAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			OperatorStatus = ex.Message;
		}
	}

	[RelayCommand]
	private async Task DeactivateOperatorAsync()
	{
		if ((object)SelectedOperator == null || _currentSession.User == null || !CanManageOperators || !_confirmationService.Confirm("Deactivate operator '" + SelectedOperator.UserName + "'?", "Deactivate operator"))
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<OperatorAccountService>().SetActiveAsync(SelectedOperator.Id, isActive: false, _currentSession.User.Id, _currentSession.User.Role);
			OperatorStatus = "Deactivated " + SelectedOperator.UserName + ".";
			await LoadOperatorsAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			OperatorStatus = ex.Message;
		}
	}

	[RelayCommand]
	private async Task ReactivateOperatorAsync()
	{
		if ((object)SelectedOperator == null || _currentSession.User == null || !CanManageOperators)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<OperatorAccountService>().SetActiveAsync(SelectedOperator.Id, isActive: true, _currentSession.User.Id, _currentSession.User.Role);
			OperatorStatus = "Reactivated " + SelectedOperator.UserName + ".";
			await LoadOperatorsAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			OperatorStatus = ex.Message;
		}
	}

	[RelayCommand]
	private async Task ChangeOperatorRoleAsync()
	{
		if ((object)SelectedOperator == null || _currentSession.User == null || !CanManageOperators)
		{
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<OperatorAccountService>().ChangeRoleAsync(SelectedOperator.Id, NewOperatorRole, _currentSession.User.Id, _currentSession.User.Role);
			OperatorStatus = "Role updated for " + SelectedOperator.UserName + ".";
			await LoadOperatorsAsync(CancellationToken.None);
		}
		catch (Exception ex)
		{
			OperatorStatus = ex.Message;
		}
	}

	[RelayCommand]
	private async Task ResetOperatorPasswordAsync()
	{
		if ((object)SelectedOperator == null || _currentSession.User == null || !CanManageOperators)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(NewOperatorPassword) || NewOperatorPassword.Trim().Length < 4)
		{
			OperatorStatus = "Enter a new PIN (min 4 characters) in the New PIN field, then click Reset PIN.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<OperatorAccountService>().ResetPasswordAsync(SelectedOperator.Id, NewOperatorPassword, _currentSession.User.Id, _currentSession.User.Role);
			NewOperatorPassword = string.Empty;
			OperatorStatus = "PIN reset for " + SelectedOperator.UserName + ".";
		}
		catch (Exception ex)
		{
			OperatorStatus = ex.Message;
		}
	}

	private async Task LoadPharmacyBrandingAsync(CancellationToken cancellationToken)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmacyProfile pharmacyProfile = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
			if (pharmacyProfile == null)
			{
				return;
			}
			PharmacyStoreName = pharmacyProfile.Name;
			PharmacyAddress = pharmacyProfile.Address ?? string.Empty;
			PharmacyPhone = pharmacyProfile.Phone ?? string.Empty;
			PharmacyEmail = pharmacyProfile.Email ?? string.Empty;
			PharmacyGstin = pharmacyProfile.Gstin ?? string.Empty;
			StoreUpiId = pharmacyProfile.UpiId ?? string.Empty;
			DocumentOutputSettings documentOutputSettings = _documentOutputSettingsStore.Load();
			UpiPayeeName = documentOutputSettings.UpiPayeeName;
			ShowCustomerQrWindow = documentOutputSettings.ShowCustomerQrWindow;
		}
		catch
		{
		}
	}

	[RelayCommand]
	private async Task RefreshRetentionSummaryAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			DataRetentionActiveSummary dataRetentionActiveSummary = await scope.ServiceProvider.GetRequiredService<DataRetentionArchiveService>().GetActiveSummaryAsync(cancellationToken);
			string value = dataRetentionActiveSummary.OldestActiveUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "—";
			ActiveRecordsSummary = $"Active Records: {dataRetentionActiveSummary.ActiveRecordCount:N0} (from {value} to Today)";
			if (dataRetentionActiveSummary.Period != DataRetentionPeriod.Permanent && dataRetentionActiveSummary.CandidateArchiveCount > 0)
			{
				RetentionStatus = $"{dataRetentionActiveSummary.CandidateArchiveCount:N0} record(s) older than the retention cutoff ({dataRetentionActiveSummary.CutoffUtc.ToLocalTime():yyyy-MM-dd}) can be archived.";
			}
		}
		catch (Exception ex)
		{
			RetentionStatus = ex.Message;
		}
	}

	[RelayCommand]
	private async Task ArchivePreRetentionRecordsAsync()
	{
		if (SelectedRetentionPeriod.Period == DataRetentionPeriod.Permanent)
		{
			RetentionStatus = "Retention is Permanent / Do Not Purge. Change the period before archiving.";
		}
		else
		{
			if (!_confirmationService.Confirm("Export transactional records older than the retention window into an encrypted local archive, then soft-delete those commercial documents from the active database?\n\nPatients, medicines, batches, stock movements and statutory register entries stay in the live database. This never hard-deletes data.", "Export & Archive Pre-Retention Records"))
			{
				return;
			}
			try
			{
				IsArchiving = true;
				RetentionStatus = "Archiving…";
				using IServiceScope scope = _scopeFactory.CreateScope();
				DataRetentionArchiveService requiredService = scope.ServiceProvider.GetRequiredService<DataRetentionArchiveService>();
				string password = scope.ServiceProvider.GetRequiredService<BackupSettingsStore>().Load().Password;
				DataRetentionArchiveResult dataRetentionArchiveResult = await requiredService.ArchiveAndPruneAsync(password);
				RetentionStatus = $"Archived {dataRetentionArchiveResult.ExportedRowCount:N0} row(s); soft-deleted {dataRetentionArchiveResult.SoftDeletedRowCount:N0}. File: {dataRetentionArchiveResult.ArchivePath}";
				await RefreshRetentionSummaryAsync();
			}
			catch (Exception ex)
			{
				RetentionStatus = ex.Message;
			}
			finally
			{
				IsArchiving = false;
			}
		}
	}

	[RelayCommand]
	private async Task CheckForUpdatesAsync()
	{
		if (IsCheckingForUpdates)
		{
			return;
		}
		IsCheckingForUpdates = true;
		UpdateStatus = "Checking…";
		try
		{
			AppUpdateAvailability appUpdateAvailability = await _appUpdates.CheckForUpdatesAsync(force: true);
			SettingsPageViewModel settingsPageViewModel = this;
			string updateStatus;
			if (appUpdateAvailability.IsUpdateAvailable)
			{
				updateStatus = appUpdateAvailability.StatusMessage + " Use Update Now on the banner, or open the download link.";
			}
			else
			{
				updateStatus = (string.IsNullOrWhiteSpace(appUpdateAvailability.StatusMessage) ? "Up to date ✓" : appUpdateAvailability.StatusMessage);
			}
			settingsPageViewModel.UpdateStatus = updateStatus;
		}
		catch (Exception ex)
		{
			UpdateStatus = "Could not check for updates: " + ex.Message;
		}
		finally
		{
			IsCheckingForUpdates = false;
		}
	}

	[RelayCommand]
	private void ToggleReleaseNotes()
	{
		if (!IsReleaseNotesVisible)
		{
			ReleaseNotesText = _versionInfo.LoadReleaseNotes();
		}
		IsReleaseNotesVisible = !IsReleaseNotesVisible;
	}

	[RelayCommand]
	private void OpenInteractiveUserManual()
	{
		ManualHubStatus = string.Empty;
		_userManualDialogs.ShowInteractiveGuide();
	}

	[RelayCommand]
	private void OpenPdfUserManual()
	{
		ManualHubStatus = _userManualDialogs.OpenPdfManual();
		UpdateStatus = ManualHubStatus;
	}

	[RelayCommand]
	private async Task EmailUserManualAgainAsync()
	{
		ManualEmailMissingMessage = string.Empty;
		ManualHubStatus = string.Empty;
		UpdateStatus = "Sending user manual…";
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmacyProfile pharmacyProfile = await (from item in scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking()
				orderby item.CreatedAtUtc
				select item).FirstOrDefaultAsync();
			string email = FirstNonEmpty(PharmacyEmail, pharmacyProfile?.Email, _currentSession.User?.Email);
			if (string.IsNullOrWhiteSpace(email))
			{
				UpdateStatus = string.Empty;
				ManualEmailMissingMessage = "Pharmacy / Billing Email is empty. Add it under General & Profile → Firm Profile, then try again.";
				SelectSettingsCategory("About");
				return;
			}
			string ownerName = _currentSession.User?.DisplayName ?? pharmacyProfile?.CompetentPersonName ?? "Pharmacist";
			UpdateStatus = await _registrationNotifications.SendWelcomeManualAsync(email, pharmacyProfile?.Name ?? PharmacyStoreName ?? "your pharmacy", ownerName, forceResend: true);
			ManualHubStatus = (string.IsNullOrWhiteSpace(UpdateStatus) ? ("User manual emailed to " + email + ".") : UpdateStatus);
		}
		catch (Exception ex)
		{
			UpdateStatus = ex.Message;
			ManualHubStatus = ex.Message;
		}
	}

	private static string? FirstNonEmpty(params string?[] values)
	{
		return values.FirstOrDefault((string value) => !string.IsNullOrWhiteSpace(value))?.Trim();
	}

	[RelayCommand]
	private void PickLogo()
	{
		OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = "Select pharmacy logo",
			Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
			CheckFileExists = true,
			Multiselect = false
		};
		if (openFileDialog.ShowDialog() == true)
		{
			LogoPath = openFileDialog.FileName;
		}
	}

	[RelayCommand]
	private async Task SaveDocumentOutputSettings()
	{
		if (!string.IsNullOrWhiteSpace(InspectorPinInput))
		{
			AppUser? user = _currentSession.User;
			if (user == null || user.Role != UserRole.Owner)
			{
				DocumentOutputStatus = "Only the owner can set the separate inspector PIN.";
				return;
			}
			if (InspectorPinInput.Length < 4 || InspectorPinInput != InspectorPinConfirmation)
			{
				DocumentOutputStatus = "Inspector PINs must match and contain at least four characters.";
				return;
			}
			_documentOutputSettingsStore.SaveInspectorPin(InspectorPinInput);
		}
		DocumentPaperSize retailMemoPaperSize = ((DefaultInvoiceTemplate == InvoiceTemplateType.Thermal80mm) ? DocumentPaperSize.Thermal80 : RetailMemoPaperSize);
		DocumentPaperSize retailInvoicePaperSize = ((DefaultInvoiceTemplate == InvoiceTemplateType.Thermal80mm) ? DocumentPaperSize.Thermal80 : ((RetailInvoicePaperSize != DocumentPaperSize.Thermal80) ? RetailInvoicePaperSize : DocumentPaperSize.A4));
		_documentOutputSettings = new DocumentOutputSettings
		{
			RetailMemoPaperSize = retailMemoPaperSize,
			RetailInvoicePaperSize = retailInvoicePaperSize,
			RetailMemoPrinter = ((RetailMemoPrinter == "(Windows default)") ? null : RetailMemoPrinter),
			RetailInvoicePrinter = ((RetailInvoicePrinter == "(Windows default)") ? null : RetailInvoicePrinter),
			FooterText = FooterText.Trim(),
			LogoPath = (string.IsNullOrWhiteSpace(LogoPath) ? null : LogoPath),
			WhatsAppNumber = WhatsAppNumber.Trim(),
			SmtpHost = SmtpHost.Trim(),
			SmtpPort = SmtpPort,
			SmtpFromAddress = SmtpFromAddress.Trim(),
			SmtpUserName = SmtpUserName.Trim(),
			SmtpEnableSsl = SmtpEnableSsl,
			CashDrawerPulse = CashDrawerPulse,
			DefaultInvoiceTemplate = DefaultInvoiceTemplate,
			SilentPrint = SilentPrint,
			ShowCustomerQrWindow = ShowCustomerQrWindow,
			UpiPayeeName = UpiPayeeName.Trim(),
			PrintDoctorName = PrintDoctorName,
			PrintCustomerPhone = PrintCustomerPhone,
			PrintShopLogo = PrintShopLogo,
			PrintTermsDisclaimer = PrintTermsDisclaimer,
			AddressLine2 = PrintAddressLine2.Trim(),
			FssaiNumber = FssaiNumber.Trim(),
			DrugLicence20B = DrugLicence20B.Trim(),
			DrugLicence21B = DrugLicence21B.Trim(),
			TermsAndDisclaimer = TermsAndDisclaimer.Trim(),
			InspectorPinHash = (string.IsNullOrWhiteSpace(InspectorPinInput) ? _documentOutputSettings.InspectorPinHash : null)
		};
		if (!string.IsNullOrWhiteSpace(InspectorPinInput))
		{
			_documentOutputSettings.InspectorPinHash = _documentOutputSettingsStore.Load().InspectorPinHash;
		}
		_documentOutputSettingsStore.Save(_documentOutputSettings);
		_documentOutputSettingsStore.SaveSmtpPassword(SmtpPassword);
		SmtpPassword = string.Empty;
		InspectorPinInput = string.Empty;
		InspectorPinConfirmation = string.Empty;
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
			PharmacyProfile pharmacyProfile = await unitOfWork.Context.PharmacyProfiles.SingleOrDefaultAsync();
			if (pharmacyProfile != null && !string.IsNullOrWhiteSpace(PharmacyStoreName))
			{
				pharmacyProfile.Name = PharmacyStoreName.Trim();
				pharmacyProfile.Address = (string.IsNullOrWhiteSpace(PharmacyAddress) ? null : PharmacyAddress.Trim());
				pharmacyProfile.Phone = (string.IsNullOrWhiteSpace(PharmacyPhone) ? null : PharmacyPhone.Trim());
				pharmacyProfile.Email = (string.IsNullOrWhiteSpace(PharmacyEmail) ? null : PharmacyEmail.Trim());
				pharmacyProfile.Gstin = (string.IsNullOrWhiteSpace(PharmacyGstin) ? null : PharmacyGstin.Trim());
				if (!string.IsNullOrWhiteSpace(StoreUpiId))
				{
					pharmacyProfile.UpiId = StoreUpiId.Trim();
				}
				await unitOfWork.SaveChangesAsync();
			}
		}
		catch (Exception ex)
		{
			DocumentOutputStatus = "Print settings saved, but pharmacy profile update failed: " + ex.Message;
			return;
		}
		DocumentOutputStatus = "Print & invoice layout settings saved. ✓";
	}

	[RelayCommand]
	private void ClearSettingsSearch()
	{
		SettingsSearchQuery = string.Empty;
	}

	[RelayCommand]
	private void FocusSettingsSearch()
	{
		SettingsSearchFocusToken = Guid.NewGuid().ToString("N");
	}

	public void RevealUpiConfiguration()
	{
		SelectSettingsCategory("Payments");
		SettingsSearchQuery = "upi";
		HighlightPaymentSection = true;
		ShowPaymentSection = true;
		UpiCardRevealToken = Guid.NewGuid().ToString("N");
	}

	public void RevealFirmEmail()
	{
		SettingsSearchQuery = string.Empty;
		SelectSettingsCategory("General");
		HighlightFirmProfileSection = true;
		ShowFirmProfileSection = true;
		FirmEmailRevealToken = Guid.NewGuid().ToString("N");
	}

	[RelayCommand]
	private void SelectSettingsCategory(string? category)
	{
		if (!string.IsNullOrWhiteSpace(category))
		{
			SelectedSettingsCategory = category;
			if (!HasActiveSettingsSearch)
			{
				ApplyCategoryVisibility(category);
			}
			RefreshCategorySelectionFlags();
		}
	}

	[RelayCommand]
	private async Task SaveFirmProfileAsync()
	{
		FirmProfileStatus = string.Empty;
		ManualEmailMissingMessage = string.Empty;
		if (string.IsNullOrWhiteSpace(PharmacyStoreName))
		{
			FirmProfileStatus = "Store name is required.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
			PharmacyProfile pharmacyProfile = await unitOfWork.Context.PharmacyProfiles.SingleOrDefaultAsync();
			if (pharmacyProfile == null)
			{
				FirmProfileStatus = "Pharmacy profile not found. Complete initial setup first.";
				return;
			}
			pharmacyProfile.Name = PharmacyStoreName.Trim();
			pharmacyProfile.Address = (string.IsNullOrWhiteSpace(PharmacyAddress) ? null : PharmacyAddress.Trim());
			pharmacyProfile.Phone = (string.IsNullOrWhiteSpace(PharmacyPhone) ? null : PharmacyPhone.Trim());
			pharmacyProfile.Email = (string.IsNullOrWhiteSpace(PharmacyEmail) ? null : PharmacyEmail.Trim());
			pharmacyProfile.Gstin = (string.IsNullOrWhiteSpace(PharmacyGstin) ? null : PharmacyGstin.Trim());
			await unitOfWork.SaveChangesAsync();
			_documentOutputSettings = _documentOutputSettingsStore.Load();
			_documentOutputSettings.AddressLine2 = PrintAddressLine2.Trim();
			_documentOutputSettings.FssaiNumber = FssaiNumber.Trim();
			_documentOutputSettings.DrugLicence20B = DrugLicence20B.Trim();
			_documentOutputSettings.DrugLicence21B = DrugLicence21B.Trim();
			_documentOutputSettingsStore.Save(_documentOutputSettings);
			FirmProfileStatus = "Saved successfully ✓";
		}
		catch (Exception ex)
		{
			FirmProfileStatus = ex.Message;
		}
	}

	[RelayCommand]
	private async Task SaveUpiSettingsAsync()
	{
		UpiVpaError = string.Empty;
		UpiSettingsStatus = string.Empty;
		string vpa = StoreUpiId.Trim();
		if (!UpiPaymentPayload.IsValidVpa(vpa))
		{
			UpiVpaError = "Enter a valid UPI ID / VPA (e.g. pharmacy@okaxis or 9876543210@paytm).";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
			PharmacyProfile pharmacyProfile = await unitOfWork.Context.PharmacyProfiles.SingleOrDefaultAsync();
			if (pharmacyProfile == null)
			{
				UpiSettingsStatus = "Pharmacy profile not found. Complete initial setup first.";
				return;
			}
			pharmacyProfile.UpiId = vpa;
			if (string.IsNullOrWhiteSpace(PharmacyStoreName))
			{
				PharmacyStoreName = pharmacyProfile.Name;
			}
			await unitOfWork.SaveChangesAsync();
			_documentOutputSettings = _documentOutputSettingsStore.Load();
			_documentOutputSettings.UpiPayeeName = UpiPayeeName.Trim();
			_documentOutputSettings.ShowCustomerQrWindow = ShowCustomerQrWindow;
			_documentOutputSettingsStore.Save(_documentOutputSettings);
			StoreUpiId = vpa;
			UpiSettingsStatus = "Saved successfully ✓";
		}
		catch (Exception ex)
		{
			UpiSettingsStatus = ex.Message;
		}
	}

	[RelayCommand]
	private void PreviewUpiQr()
	{
		UpiVpaError = string.Empty;
		UpiSettingsStatus = string.Empty;
		string text = StoreUpiId.Trim();
		if (!UpiPaymentPayload.IsValidVpa(text))
		{
			UpiVpaError = "Enter a valid UPI ID / VPA before previewing the QR code.";
			return;
		}
		try
		{
			string pharmacyName;
			if (string.IsNullOrWhiteSpace(UpiPayeeName))
			{
				pharmacyName = (string.IsNullOrWhiteSpace(PharmacyStoreName) ? "Pharmacy" : PharmacyStoreName.Trim());
			}
			else
			{
				pharmacyName = UpiPayeeName.Trim();
			}
			BitmapSource qrImage = QrCodeImage.FromText(UpiPaymentPayload.Build(text, pharmacyName, 1.00m, "TEST-QR"), 600);
			_paymentQrDialogs.Show(new PaymentQrDialogRequest(text, pharmacyName, 1.00m, "TEST-QR", qrImage, ShowOnCustomerDisplay: false));
			UpiSettingsStatus = "Preview shown — scan with a UPI app to verify the VPA before billing.";
		}
		catch (Exception ex)
		{
			UpiSettingsStatus = ex.Message;
		}
	}

	[RelayCommand]
	private void OpenRelatedSettingsSection(string? sectionKey)
	{
		if (!string.IsNullOrWhiteSpace(sectionKey))
		{
			_navigationService.Navigate(sectionKey);
		}
	}

	private static IReadOnlyList<SettingsSearchEntry> BuildSettingsIndex()
	{
		return new _003C_003Ez__ReadOnlyArray<SettingsSearchEntry>(new SettingsSearchEntry[15]
		{
			new SettingsSearchEntry("appearance", "Appearance", "Theme, accent and language", "appearance theme accent language dark light ui colour color", "General"),
			new SettingsSearchEntry("businessMode", "Licensing & Mode", "Business mode and drug-licence posture", "licensing mode retail wholesale both 2-in-1 license licence subscription business production store", "General"),
			new SettingsSearchEntry("branch", "Branch identity", "Branch code, GSTIN and drug licence", "branch identity code gstin drug licence license head office invoice series", "General"),
			new SettingsSearchEntry("firmProfile", "Firm Profile", "Pharmacy name, email, DL 20/21, GSTIN and address", "firm profile pharmacy name store dl 20b 21b 20 21 fssai gstin address phone contact email billing branding gmail", "General"),
			new SettingsSearchEntry("gemini", "Gemini API", "Purchase-document AI extraction key", "gemini ai api key purchase document extraction", "General"),
			new SettingsSearchEntry("payment", "UPI & Digital Payment Settings", "Store UPI ID / VPA, payee name and customer QR", "payment upi qr code vpa digital payee merchant pos cash rounding terminal customer window modes credit ledger split cheque card store@upi", "Payments"),
			new SettingsSearchEntry("printer", "Printer & Invoicing", "Tax invoice format, thermal receipt and print options", "printer invoicing tax invoice delivery challan thermal receipt margins f10 silent print doctor terms disclaimer template a4 80mm", "Printer"),
			new SettingsSearchEntry("documentOutput", "Document output", "Paper size, printers, SMTP and logo", "document output paper size printer esc/pos cash drawer logo footer whatsapp smtp email ssl", "Printer"),
			new SettingsSearchEntry("operators", "Users & Operators", "Cashier and pharmacist accounts", "users operators cashier pharmacist admin pin password pbkdf2 role", "Users"),
			new SettingsSearchEntry("inspector", "Inspector PIN", "Owner-only read-only inspector access PIN", "inspector pin password security owner read-only", "Users"),
			new SettingsSearchEntry("dataBackup", "Data & Backup", "Retention, archive and local backup policy", "data backup retention archive usb export purge soft-delete encrypted zip", "Data"),
			new SettingsSearchEntry("sync", "Sync Station", "Local Wi-Fi pairing on port 5055", "sync station port 5055 wifi wi-fi local pairing mdns listener firewall rule android", "Data", "Sync"),
			new SettingsSearchEntry("backupNav", "Backup & Cloud", "Google Drive, USB export and auto-backup", "backup cloud google drive auto-backup exit usb export retention restore move pc sync on exit", "Data", "Backup"),
			new SettingsSearchEntry("userManual", "User Manual & Guides", "In-app quick guides, PDF manual and email dispatch", "user manual guide quick start tutorial help f1 pdf email shortcuts billing sync reports", "About"),
			new SettingsSearchEntry("about", "About PharmaBill", "Version, updates and user manual", "about version update release notes manual support email saer", "About")
		});
	}

	private void ApplySettingsSearchFilter(string? rawQuery)
	{
		string text = (rawQuery ?? string.Empty).Trim();
		HasActiveSettingsSearch = text.Length > 0;
		if (!HasActiveSettingsSearch)
		{
			ApplyCategoryVisibility(SelectedSettingsCategory);
			HasSettingsSearchResults = true;
			SettingsSearchEmptyMessage = string.Empty;
			RefreshCategorySelectionFlags();
			return;
		}
		string[] tokens = text.Split(new char[5] { ' ', ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		List<SettingsSearchEntry> list = _settingsIndex.Where((SettingsSearchEntry entry) => tokens.All((string token) => entry.Title.Contains(token, StringComparison.OrdinalIgnoreCase) || entry.Summary.Contains(token, StringComparison.OrdinalIgnoreCase) || entry.Keywords.Contains(token, StringComparison.OrdinalIgnoreCase) || entry.Id.Contains(token, StringComparison.OrdinalIgnoreCase))).ToList();
		HashSet<string> hashSet = list.Select((SettingsSearchEntry entry) => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (list.Count > 0)
		{
			SelectedSettingsCategory = list[0].Category;
		}
		HideAllSections();
		SetSection("appearance", hashSet.Contains("appearance"), hashSet.Contains("appearance"));
		SetSection("businessMode", hashSet.Contains("businessMode"), hashSet.Contains("businessMode"));
		SetSection("branch", hashSet.Contains("branch"), hashSet.Contains("branch"));
		SetSection("operators", hashSet.Contains("operators") && CanManageOperators, hashSet.Contains("operators"));
		SetSection("gemini", hashSet.Contains("gemini"), hashSet.Contains("gemini"));
		SetSection("firmProfile", hashSet.Contains("firmProfile"), hashSet.Contains("firmProfile"));
		SetSection("payment", hashSet.Contains("payment"), hashSet.Contains("payment"));
		SetSection("printer", hashSet.Contains("printer"), hashSet.Contains("printer"));
		SetSection("documentOutput", hashSet.Contains("documentOutput"), hashSet.Contains("documentOutput"));
		SetSection("inspector", hashSet.Contains("inspector"), hashSet.Contains("inspector"));
		SetSection("dataBackup", hashSet.Contains("dataBackup"), hashSet.Contains("dataBackup"));
		SetSection("userManual", hashSet.Contains("userManual"), hashSet.Contains("userManual"));
		SetSection("about", hashSet.Contains("about"), hashSet.Contains("about"));
		ShowSyncShortcut = hashSet.Contains("sync");
		HighlightSyncShortcut = ShowSyncShortcut;
		ShowBackupShortcut = hashSet.Contains("backupNav");
		HighlightBackupShortcut = ShowBackupShortcut;
		HasSettingsSearchResults = hashSet.Count > 0;
		SettingsSearchEmptyMessage = (HasSettingsSearchResults ? string.Empty : ("No settings found matching '" + text + "'. Try searching for 'payment', 'backup', or 'printer'."));
		RefreshCategorySelectionFlags();
	}

	private void ApplyCategoryVisibility(string category)
	{
		HideAllSections();
		switch (category)
		{
		case "General":
			SetSection("appearance", visible: true, highlight: false);
			SetSection("businessMode", visible: true, highlight: false);
			SetSection("firmProfile", visible: true, highlight: false);
			SetSection("branch", visible: true, highlight: false);
			SetSection("gemini", visible: true, highlight: false);
			break;
		case "Payments":
			SetSection("payment", visible: true, highlight: false);
			break;
		case "Printer":
			SetSection("printer", visible: true, highlight: false);
			SetSection("documentOutput", visible: true, highlight: false);
			break;
		case "Users":
			SetSection("operators", CanManageOperators, highlight: false);
			SetSection("inspector", visible: true, highlight: false);
			break;
		case "Data":
			SetSection("dataBackup", visible: true, highlight: false);
			ShowSyncShortcut = true;
			ShowBackupShortcut = true;
			HighlightSyncShortcut = false;
			HighlightBackupShortcut = false;
			break;
		case "About":
			SetSection("userManual", visible: true, highlight: false);
			SetSection("about", visible: true, highlight: false);
			break;
		default:
			SetSection("appearance", visible: true, highlight: false);
			break;
		}
	}

	private void HideAllSections()
	{
		SetSection("appearance", visible: false, highlight: false);
		SetSection("businessMode", visible: false, highlight: false);
		SetSection("branch", visible: false, highlight: false);
		SetSection("operators", visible: false, highlight: false);
		SetSection("gemini", visible: false, highlight: false);
		SetSection("firmProfile", visible: false, highlight: false);
		SetSection("payment", visible: false, highlight: false);
		SetSection("printer", visible: false, highlight: false);
		SetSection("documentOutput", visible: false, highlight: false);
		SetSection("inspector", visible: false, highlight: false);
		SetSection("dataBackup", visible: false, highlight: false);
		SetSection("userManual", visible: false, highlight: false);
		SetSection("about", visible: false, highlight: false);
		ShowSyncShortcut = false;
		HighlightSyncShortcut = false;
		ShowBackupShortcut = false;
		HighlightBackupShortcut = false;
	}

	private void RefreshCategorySelectionFlags()
	{
		IsGeneralCategorySelected = SelectedSettingsCategory == "General";
		IsPaymentsCategorySelected = SelectedSettingsCategory == "Payments";
		IsPrinterCategorySelected = SelectedSettingsCategory == "Printer";
		IsUsersCategorySelected = SelectedSettingsCategory == "Users";
		IsDataCategorySelected = SelectedSettingsCategory == "Data";
		IsAboutCategorySelected = SelectedSettingsCategory == "About";
	}

	private void SetSection(string id, bool visible, bool highlight)
	{
		if (id == null)
		{
			return;
		}
		switch (id.Length)
		{
		case 10:
			switch (id[0])
			{
			case 'a':
				if (id == "appearance")
				{
					ShowAppearanceSection = visible;
					HighlightAppearanceSection = highlight;
				}
				break;
			case 'd':
				if (id == "dataBackup")
				{
					ShowDataBackupSection = visible;
					HighlightDataBackupSection = highlight;
				}
				break;
			case 'u':
				if (id == "userManual")
				{
					ShowUserManualSection = visible;
					HighlightUserManualSection = highlight;
				}
				break;
			}
			break;
		case 6:
			switch (id[0])
			{
			case 'b':
				if (id == "branch")
				{
					ShowBranchSection = visible;
					HighlightBranchSection = highlight;
				}
				break;
			case 'g':
				if (id == "gemini")
				{
					ShowGeminiSection = visible;
					HighlightGeminiSection = highlight;
				}
				break;
			}
			break;
		case 9:
			switch (id[0])
			{
			case 'o':
				if (id == "operators")
				{
					ShowOperatorsSection = visible;
					HighlightOperatorsSection = highlight;
				}
				break;
			case 'i':
				if (id == "inspector")
				{
					ShowInspectorSection = visible;
					HighlightInspectorSection = highlight;
				}
				break;
			}
			break;
		case 7:
			switch (id[1])
			{
			case 'a':
				if (id == "payment")
				{
					ShowPaymentSection = visible;
					HighlightPaymentSection = highlight;
				}
				break;
			case 'r':
				if (id == "printer")
				{
					ShowPrinterSection = visible;
					HighlightPrinterSection = highlight;
				}
				break;
			}
			break;
		case 12:
			if (id == "businessMode")
			{
				ShowBusinessModeSection = visible;
				HighlightBusinessModeSection = highlight;
			}
			break;
		case 11:
			if (id == "firmProfile")
			{
				ShowFirmProfileSection = visible;
				HighlightFirmProfileSection = highlight;
			}
			break;
		case 14:
			if (id == "documentOutput")
			{
				ShowDocumentOutputSection = visible;
				HighlightDocumentOutputSection = highlight;
			}
			break;
		case 5:
			if (id == "about")
			{
				ShowAboutSection = visible;
				HighlightAboutSection = highlight;
			}
			break;
		case 8:
		case 13:
			break;
		}
	}

	[RelayCommand]
	private void RemoveSmtpPassword()
	{
		_documentOutputSettingsStore.DeleteSmtpPassword();
		SmtpPassword = string.Empty;
		DocumentOutputStatus = "Saved SMTP password removed.";
	}

	private static ObservableCollection<string> LoadPrinters()
	{
		ObservableCollection<string> observableCollection = new ObservableCollection<string> { "(Windows default)" };
		using LocalPrintServer localPrintServer = new LocalPrintServer();
		foreach (PrintQueue printQueue in localPrintServer.GetPrintQueues())
		{
			if (!observableCollection.Contains(printQueue.Name, StringComparer.OrdinalIgnoreCase))
			{
				observableCollection.Add(printQueue.Name);
			}
		}
		return observableCollection;
	}

	[RelayCommand]
	private void SaveGeminiApiKey()
	{
		if (string.IsNullOrWhiteSpace(GeminiApiKeyInput))
		{
			GeminiApiKeyStatus = "Enter a Gemini API key.";
			return;
		}
		_geminiApiKeyStore.Save(GeminiApiKeyInput);
		GeminiApiKeyInput = string.Empty;
		GeminiApiKeyStatus = "Gemini API key saved to this Windows user profile.";
	}

	[RelayCommand]
	private void RemoveGeminiApiKey()
	{
		_geminiApiKeyStore.Delete();
		GeminiApiKeyInput = string.Empty;
		GeminiApiKeyStatus = "Gemini API key removed.";
	}

	[RelayCommand]
	private async Task ChangeBusinessModeAsync()
	{
		ModeError = string.Empty;
		if (_currentSession.User == null || !PermissionMatrix.Allows(_currentSession.User.Role, AppPermission.ChangeBusinessMode))
		{
			ModeError = "Only the owner or manager can change the business mode.";
			return;
		}
		using IServiceScope scope = _scopeFactory.CreateScope();
		BusinessModeService modeService = scope.ServiceProvider.GetRequiredService<BusinessModeService>();
		IReadOnlyList<string> readOnlyList = await modeService.GetMissingRequirementsAsync(SelectedBusinessMode);
		if (readOnlyList.Count > 0)
		{
			ModeError = "Mode change blocked. Missing: " + string.Join(", ", readOnlyList) + ".";
		}
		else if (_confirmationService.Confirm($"Change the pharmacy business mode to {SelectedBusinessMode}? This will be recorded in the audit log.", "Confirm business mode change"))
		{
			try
			{
				await modeService.ChangeModeAsync(SelectedBusinessMode, _currentSession.User.Id);
				BusinessModeChanged?.Invoke(this, EventArgs.Empty);
				return;
			}
			catch (Exception ex) when ((ex is InvalidOperationException || ex is UnauthorizedAccessException) ? true : false)
			{
				ModeError = ex.Message;
				return;
			}
		}
	}

	private void OnLanguageChanged(object? sender, EventArgs e)
	{
		Title = _languageService.GetString("NavSettings");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedRetentionPeriodChanged(RetentionPeriodChoice value)
	{
		if (!_suppressRetentionSave && (object)value != null)
		{
			DataRetentionSettings dataRetentionSettings = _retentionSettingsStore.Load();
			_retentionSettingsStore.Save(dataRetentionSettings with
			{
				Period = value.Period
			});
			RetentionStatus = "Retention period saved: " + value.DisplayName + ".";
			RefreshRetentionSummaryAsync();
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSettingsSearchQueryChanged(string value)
	{
		ApplySettingsSearchFilter(value);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedThemeChanged(string value)
	{
		_themeService.ApplyTheme(value);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedAccentChanged(string value)
	{
		_themeService.ApplyAccent(value);
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedLanguageChanged(string value)
	{
		_languageService.ApplyLanguage(value);
	}
}
