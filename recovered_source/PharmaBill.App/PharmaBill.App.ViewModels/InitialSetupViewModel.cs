using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Services.Auth;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class InitialSetupViewModel : ObservableObject
{
	private readonly PharmacySetupService _setupService;

	private readonly IFilePickerService _filePicker;

	private readonly RecoveryCodeStore _recoveryCodeStore;

	private readonly SocialLoginCoordinator _socialLoginCoordinator;

	private readonly CurrentSession _currentSession;

	private readonly RegistrationNotificationService _registrationNotifications;

	[ObservableProperty]
	private BusinessMode _selectedBusinessMode;

	[ObservableProperty]
	private string _pharmacyName = string.Empty;

	[ObservableProperty]
	private string _legalName = string.Empty;

	[ObservableProperty]
	private string _address = string.Empty;

	[ObservableProperty]
	private string _phone = string.Empty;

	[ObservableProperty]
	private string _email = string.Empty;

	[ObservableProperty]
	private string _gstin = string.Empty;

	[ObservableProperty]
	private string _drugLicence20B = string.Empty;

	[ObservableProperty]
	private string _drugLicence21B = string.Empty;

	[ObservableProperty]
	private DateTime? _drugLicence20IssueDate;

	[ObservableProperty]
	private DateTime? _drugLicence20ExpiryDate;

	[ObservableProperty]
	private DateTime? _drugLicence21IssueDate;

	[ObservableProperty]
	private DateTime? _drugLicence21ExpiryDate;

	[ObservableProperty]
	private string _pan = string.Empty;

	[ObservableProperty]
	private string _competentPersonName = string.Empty;

	[ObservableProperty]
	private string _competentPersonQualification = string.Empty;

	[ObservableProperty]
	private string _competentPersonRegistrationNumber = string.Empty;

	[ObservableProperty]
	private string _bankName = string.Empty;

	[ObservableProperty]
	private string _bankAccountName = string.Empty;

	[ObservableProperty]
	private string _bankAccountNumber = string.Empty;

	[ObservableProperty]
	private string _bankIfsc = string.Empty;

	[ObservableProperty]
	private string _upiId = string.Empty;

	[ObservableProperty]
	private string _invoicePrefix = "WIN1";

	[ObservableProperty]
	private string _adminFullName = string.Empty;

	[ObservableProperty]
	private string _adminUsername = string.Empty;

	[ObservableProperty]
	private string _adminPhone = string.Empty;

	[ObservableProperty]
	private string _adminSecret = string.Empty;

	[ObservableProperty]
	private string _adminSecretConfirmation = string.Empty;

	[ObservableProperty]
	private string _idleLockMinutes = "10";

	[ObservableProperty]
	private string _wholesaleLicenceType = string.Empty;

	[ObservableProperty]
	private string _newLicenceType = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private bool _isSocialOnboarding;

	[ObservableProperty]
	[NotifyPropertyChangedFor("CanUseSocialSignIn")]
	private bool _isSocialBusy;

	[ObservableProperty]
	private string? _socialAuthProvider;

	[ObservableProperty]
	private string? _socialSubjectId;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? signInWithGoogleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addLicenceTypeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addLicenceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addRetail20Command;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addRetail21Command;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<LicenceDraftViewModel?>? selectDocumentCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<LicenceDraftViewModel?>? removeLicenceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? completeSetupCommand;

	public ObservableCollection<string> LicenceTypeOptions { get; }

	public ObservableCollection<LicenceDraftViewModel> Licences { get; } = new ObservableCollection<LicenceDraftViewModel>();

	public string RecoveryCode { get; private set; } = string.Empty;

	public bool CanUseSocialSignIn => !IsSocialBusy;

	public string PageTitle => SelectedBusinessMode switch
	{
		BusinessMode.Wholesaler => "Set up your Wholesale Distribution", 
		BusinessMode.Both => "Set up your Pharmacy & Wholesale Enterprise", 
		_ => "Set up your pharmacy", 
	};

	public string DetailsGroupHeader => SelectedBusinessMode switch
	{
		BusinessMode.Wholesaler => "Distributor & Firm Details", 
		BusinessMode.Both => "Pharmacy & Wholesale Enterprise Details", 
		_ => "Pharmacy details", 
	};

	public string NameFieldLabel => SelectedBusinessMode switch
	{
		BusinessMode.Wholesaler => "Firm / Wholesaler Name *", 
		BusinessMode.Both => "Pharmacy / Firm Name *", 
		_ => "Pharmacy / store name *", 
	};

	public string Licence20Label => SelectedBusinessMode switch
	{
		BusinessMode.Wholesaler => "Wholesale Drug Licence (Form 20B)", 
		BusinessMode.Both => "Drug Licence Form 20 / 20B", 
		_ => "Drug licence Form 20", 
	};

	public string Licence21Label => SelectedBusinessMode switch
	{
		BusinessMode.Wholesaler => "Form 21B Licence", 
		BusinessMode.Both => "Drug Licence Form 21 / 21B", 
		_ => "Drug licence Form 21", 
	};

	public string Licence20Placeholder
	{
		get
		{
			if (SelectedBusinessMode != BusinessMode.Wholesaler)
			{
				return "DL No. 20/...";
			}
			return "DL No. 20B/...";
		}
	}

	public string Licence21Placeholder
	{
		get
		{
			if (SelectedBusinessMode != BusinessMode.Wholesaler)
			{
				return "DL No. 21/...";
			}
			return "DL No. 21B/...";
		}
	}

	public bool IsRetailStoreSelected
	{
		get
		{
			return SelectedBusinessMode == BusinessMode.Retail;
		}
		set
		{
			if (value)
			{
				SelectedBusinessMode = BusinessMode.Retail;
			}
		}
	}

	public bool IsWholesaleStoreSelected
	{
		get
		{
			return SelectedBusinessMode == BusinessMode.Wholesaler;
		}
		set
		{
			if (value)
			{
				SelectedBusinessMode = BusinessMode.Wholesaler;
			}
		}
	}

	public bool IsBothStoreSelected
	{
		get
		{
			return SelectedBusinessMode == BusinessMode.Both;
		}
		set
		{
			if (value)
			{
				SelectedBusinessMode = BusinessMode.Both;
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
				OnSelectedBusinessModeChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedBusinessMode);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PharmacyName);
				_pharmacyName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PharmacyName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string LegalName
	{
		get
		{
			return _legalName;
		}
		[MemberNotNull("_legalName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_legalName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LegalName);
				_legalName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LegalName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Address
	{
		get
		{
			return _address;
		}
		[MemberNotNull("_address")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_address, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Address);
				_address = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Address);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Phone
	{
		get
		{
			return _phone;
		}
		[MemberNotNull("_phone")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_phone, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Phone);
				_phone = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Phone);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Email
	{
		get
		{
			return _email;
		}
		[MemberNotNull("_email")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_email, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Email);
				_email = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Email);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Gstin
	{
		get
		{
			return _gstin;
		}
		[MemberNotNull("_gstin")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_gstin, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Gstin);
				_gstin = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Gstin);
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
	public DateTime? DrugLicence20IssueDate
	{
		get
		{
			return _drugLicence20IssueDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_drugLicence20IssueDate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DrugLicence20IssueDate);
				_drugLicence20IssueDate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DrugLicence20IssueDate);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? DrugLicence20ExpiryDate
	{
		get
		{
			return _drugLicence20ExpiryDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_drugLicence20ExpiryDate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DrugLicence20ExpiryDate);
				_drugLicence20ExpiryDate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DrugLicence20ExpiryDate);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? DrugLicence21IssueDate
	{
		get
		{
			return _drugLicence21IssueDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_drugLicence21IssueDate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DrugLicence21IssueDate);
				_drugLicence21IssueDate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DrugLicence21IssueDate);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? DrugLicence21ExpiryDate
	{
		get
		{
			return _drugLicence21ExpiryDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_drugLicence21ExpiryDate, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.DrugLicence21ExpiryDate);
				_drugLicence21ExpiryDate = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.DrugLicence21ExpiryDate);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Pan
	{
		get
		{
			return _pan;
		}
		[MemberNotNull("_pan")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_pan, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Pan);
				_pan = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Pan);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CompetentPersonName
	{
		get
		{
			return _competentPersonName;
		}
		[MemberNotNull("_competentPersonName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_competentPersonName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CompetentPersonName);
				_competentPersonName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CompetentPersonName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CompetentPersonQualification
	{
		get
		{
			return _competentPersonQualification;
		}
		[MemberNotNull("_competentPersonQualification")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_competentPersonQualification, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CompetentPersonQualification);
				_competentPersonQualification = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CompetentPersonQualification);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CompetentPersonRegistrationNumber
	{
		get
		{
			return _competentPersonRegistrationNumber;
		}
		[MemberNotNull("_competentPersonRegistrationNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_competentPersonRegistrationNumber, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CompetentPersonRegistrationNumber);
				_competentPersonRegistrationNumber = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CompetentPersonRegistrationNumber);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BankName
	{
		get
		{
			return _bankName;
		}
		[MemberNotNull("_bankName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_bankName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BankName);
				_bankName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BankName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BankAccountName
	{
		get
		{
			return _bankAccountName;
		}
		[MemberNotNull("_bankAccountName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_bankAccountName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BankAccountName);
				_bankAccountName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BankAccountName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BankAccountNumber
	{
		get
		{
			return _bankAccountNumber;
		}
		[MemberNotNull("_bankAccountNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_bankAccountNumber, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BankAccountNumber);
				_bankAccountNumber = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BankAccountNumber);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BankIfsc
	{
		get
		{
			return _bankIfsc;
		}
		[MemberNotNull("_bankIfsc")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_bankIfsc, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.BankIfsc);
				_bankIfsc = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.BankIfsc);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpiId
	{
		get
		{
			return _upiId;
		}
		[MemberNotNull("_upiId")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_upiId, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.UpiId);
				_upiId = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.UpiId);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string InvoicePrefix
	{
		get
		{
			return _invoicePrefix;
		}
		[MemberNotNull("_invoicePrefix")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_invoicePrefix, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.InvoicePrefix);
				_invoicePrefix = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.InvoicePrefix);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AdminFullName
	{
		get
		{
			return _adminFullName;
		}
		[MemberNotNull("_adminFullName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_adminFullName, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AdminFullName);
				_adminFullName = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AdminFullName);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AdminUsername
	{
		get
		{
			return _adminUsername;
		}
		[MemberNotNull("_adminUsername")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_adminUsername, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AdminUsername);
				_adminUsername = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AdminUsername);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AdminPhone
	{
		get
		{
			return _adminPhone;
		}
		[MemberNotNull("_adminPhone")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_adminPhone, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AdminPhone);
				_adminPhone = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AdminPhone);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AdminSecret
	{
		get
		{
			return _adminSecret;
		}
		[MemberNotNull("_adminSecret")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_adminSecret, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AdminSecret);
				_adminSecret = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AdminSecret);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string AdminSecretConfirmation
	{
		get
		{
			return _adminSecretConfirmation;
		}
		[MemberNotNull("_adminSecretConfirmation")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_adminSecretConfirmation, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.AdminSecretConfirmation);
				_adminSecretConfirmation = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.AdminSecretConfirmation);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string IdleLockMinutes
	{
		get
		{
			return _idleLockMinutes;
		}
		[MemberNotNull("_idleLockMinutes")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_idleLockMinutes, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IdleLockMinutes);
				_idleLockMinutes = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IdleLockMinutes);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string WholesaleLicenceType
	{
		get
		{
			return _wholesaleLicenceType;
		}
		[MemberNotNull("_wholesaleLicenceType")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_wholesaleLicenceType, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.WholesaleLicenceType);
				_wholesaleLicenceType = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.WholesaleLicenceType);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string NewLicenceType
	{
		get
		{
			return _newLicenceType;
		}
		[MemberNotNull("_newLicenceType")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_newLicenceType, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.NewLicenceType);
				_newLicenceType = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.NewLicenceType);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ErrorMessage
	{
		get
		{
			return _errorMessage;
		}
		[MemberNotNull("_errorMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_errorMessage, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ErrorMessage);
				_errorMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ErrorMessage);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSocialOnboarding
	{
		get
		{
			return _isSocialOnboarding;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSocialOnboarding, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsSocialOnboarding);
				_isSocialOnboarding = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsSocialOnboarding);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSocialBusy
	{
		get
		{
			return _isSocialBusy;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSocialBusy, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsSocialBusy);
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CanUseSocialSignIn);
				_isSocialBusy = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsSocialBusy);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CanUseSocialSignIn);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? SocialAuthProvider
	{
		get
		{
			return _socialAuthProvider;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_socialAuthProvider, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SocialAuthProvider);
				_socialAuthProvider = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SocialAuthProvider);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? SocialSubjectId
	{
		get
		{
			return _socialSubjectId;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_socialSubjectId, value))
			{
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SocialSubjectId);
				_socialSubjectId = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SocialSubjectId);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SignInWithGoogleCommand => signInWithGoogleCommand ?? (signInWithGoogleCommand = new AsyncRelayCommand(SignInWithGoogleAsync, () => CanUseSocialSignIn));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddLicenceTypeCommand => addLicenceTypeCommand ?? (addLicenceTypeCommand = new RelayCommand(AddLicenceType));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddLicenceCommand => addLicenceCommand ?? (addLicenceCommand = new RelayCommand(AddLicence));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddRetail20Command => addRetail20Command ?? (addRetail20Command = new RelayCommand(AddRetail20));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddRetail21Command => addRetail21Command ?? (addRetail21Command = new RelayCommand(AddRetail21));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<LicenceDraftViewModel?> SelectDocumentCommand => selectDocumentCommand ?? (selectDocumentCommand = new RelayCommand<LicenceDraftViewModel>(SelectDocument));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<LicenceDraftViewModel?> RemoveLicenceCommand => removeLicenceCommand ?? (removeLicenceCommand = new RelayCommand<LicenceDraftViewModel>(RemoveLicence));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand CompleteSetupCommand => completeSetupCommand ?? (completeSetupCommand = new AsyncRelayCommand(CompleteSetupAsync));

	public event EventHandler? SetupCompleted;

	public InitialSetupViewModel(PharmacySetupService setupService, IFilePickerService filePicker, RecoveryCodeStore recoveryCodeStore, SocialLoginCoordinator socialLoginCoordinator, CurrentSession currentSession, RegistrationNotificationService registrationNotifications)
	{
		_setupService = setupService;
		_filePicker = filePicker;
		_recoveryCodeStore = recoveryCodeStore;
		_socialLoginCoordinator = socialLoginCoordinator;
		_currentSession = currentSession;
		_registrationNotifications = registrationNotifications;
		LicenceTypeOptions = new ObservableCollection<string> { "20", "21" };
	}

	public void ApplySocialPrefill(SocialAuthProfile profile)
	{
		IsSocialOnboarding = true;
		SocialAuthProvider = profile.Provider;
		SocialSubjectId = profile.SubjectId;
		AdminFullName = (string.IsNullOrWhiteSpace(profile.Name) ? (profile.Email ?? string.Empty) : profile.Name);
		Email = profile.Email ?? string.Empty;
		AdminUsername = DeriveUsername(profile.Email);
		ErrorMessage = string.Empty;
	}

	[RelayCommand(CanExecute = "CanUseSocialSignIn")]
	private async Task SignInWithGoogleAsync()
	{
		await HandleSocialSignInAsync(await _socialLoginCoordinator.SignInWithGoogleAsync());
	}

	private async Task HandleSocialSignInAsync(SocialLoginOutcome outcome)
	{
		IsSocialBusy = true;
		try
		{
			ErrorMessage = string.Empty;
			switch (outcome.Kind)
			{
			case SocialLoginOutcomeKind.SignedIn:
				if (_currentSession.User != null)
				{
					SetupCompleted?.Invoke(_currentSession.User, EventArgs.Empty);
					return;
				}
				break;
			case SocialLoginOutcomeKind.SetupRequired:
				if ((object)outcome.Profile != null)
				{
					ApplySocialPrefill(outcome.Profile);
					return;
				}
				break;
			case SocialLoginOutcomeKind.LinkAdminRequired:
				ErrorMessage = "This Google account is not linked yet. Close setup and use Unlock → Continue with Google to link it to the primary Admin.";
				return;
			case SocialLoginOutcomeKind.Offline:
			case SocialLoginOutcomeKind.Error:
				ErrorMessage = outcome.Message ?? "Social sign-in failed.";
				return;
			}
		}
		finally
		{
			IsSocialBusy = false;
		}
		await Task.CompletedTask;
	}

	[RelayCommand]
	private void AddLicenceType()
	{
		string text = NewLicenceType.Trim();
		if (text.Length == 0)
		{
			throw new InvalidOperationException("Enter a licence type before adding it.");
		}
		if (!LicenceTypeOptions.Contains(text, StringComparer.OrdinalIgnoreCase))
		{
			LicenceTypeOptions.Add(text);
		}
		WholesaleLicenceType = text;
		NewLicenceType = string.Empty;
	}

	[RelayCommand]
	private void AddLicence()
	{
		string wholesaleLicenceType = WholesaleLicenceType;
		if (string.IsNullOrWhiteSpace(wholesaleLicenceType))
		{
			throw new InvalidOperationException("Select an editable wholesale licence type first.");
		}
		Licences.Add(new LicenceDraftViewModel
		{
			LicenceType = wholesaleLicenceType
		});
	}

	[RelayCommand]
	private void AddRetail20()
	{
		Licences.Add(new LicenceDraftViewModel
		{
			LicenceType = "20"
		});
	}

	[RelayCommand]
	private void AddRetail21()
	{
		Licences.Add(new LicenceDraftViewModel
		{
			LicenceType = "21"
		});
	}

	[RelayCommand]
	private void SelectDocument(LicenceDraftViewModel? licence)
	{
		if (licence != null)
		{
			licence.DocumentPath = _filePicker.PickLicenceDocument();
		}
	}

	[RelayCommand]
	private void RemoveLicence(LicenceDraftViewModel? licence)
	{
		if (licence != null)
		{
			Licences.Remove(licence);
		}
	}

	[RelayCommand]
	private async Task CompleteSetupAsync()
	{
		ErrorMessage = string.Empty;
		if (string.IsNullOrWhiteSpace(PharmacyName) || string.IsNullOrWhiteSpace(AdminFullName) || string.IsNullOrWhiteSpace(AdminUsername))
		{
			ErrorMessage = ((SelectedBusinessMode == BusinessMode.Wholesaler) ? "Enter firm name, admin full name, and username." : "Enter pharmacy name, admin full name, and username.");
			return;
		}
		if (AdminSecret.Length < 4)
		{
			ErrorMessage = "PIN or password must contain at least 4 characters.";
			return;
		}
		if (AdminSecret != AdminSecretConfirmation)
		{
			ErrorMessage = "PIN or password confirmation does not match.";
			return;
		}
		bool flag = !int.TryParse(IdleLockMinutes, out var result);
		if (!flag)
		{
			bool flag2 = ((result < 1 || result > 240) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			ErrorMessage = "Lock timeout must be between 1 and 240 minutes.";
			return;
		}
		try
		{
			EnsureQuickLicences();
			PharmacyProfile profile = new PharmacyProfile
			{
				Name = PharmacyName.Trim(),
				LegalName = NullIfWhiteSpace(LegalName),
				Address = NullIfWhiteSpace(Address),
				Phone = NullIfWhiteSpace(Phone),
				Email = NullIfWhiteSpace(Email),
				Gstin = NullIfWhiteSpace(Gstin),
				Pan = NullIfWhiteSpace(Pan),
				BusinessMode = SelectedBusinessMode,
				CompetentPersonName = NullIfWhiteSpace(CompetentPersonName),
				CompetentPersonQualification = NullIfWhiteSpace(CompetentPersonQualification),
				CompetentPersonRegistrationNumber = NullIfWhiteSpace(CompetentPersonRegistrationNumber),
				BankName = NullIfWhiteSpace(BankName),
				BankAccountName = NullIfWhiteSpace(BankAccountName),
				BankAccountNumber = NullIfWhiteSpace(BankAccountNumber),
				BankIfsc = NullIfWhiteSpace(BankIfsc),
				UpiId = NullIfWhiteSpace(UpiId),
				InvoicePrefix = (string.IsNullOrWhiteSpace(InvoicePrefix) ? "WIN1" : InvoicePrefix.Trim()),
				WholesaleLicenceTypesJson = JsonSerializer.Serialize(LicenceTypeOptions.Where((string type) => !(type == "20") && !(type == "21")).ToArray())
			};
			AppUser admin = new AppUser
			{
				DisplayName = AdminFullName.Trim(),
				UserName = AdminUsername.Trim(),
				Phone = NullIfWhiteSpace(AdminPhone),
				Email = NullIfWhiteSpace(Email),
				Role = UserRole.Owner,
				IdleLockMinutes = result,
				AuthProvider = NullIfWhiteSpace(SocialAuthProvider ?? string.Empty),
				ProviderSubjectId = NullIfWhiteSpace(SocialSubjectId ?? string.Empty)
			};
			RecoveryCode = await _recoveryCodeStore.CreateAsync();
			try
			{
				await _setupService.CompleteSetupAsync(profile, Licences.Select((LicenceDraftViewModel licence) => licence.ToEntity()), admin, AdminSecret);
			}
			catch
			{
				await _recoveryCodeStore.DeleteAsync();
				RecoveryCode = string.Empty;
				throw;
			}
			if (!string.IsNullOrWhiteSpace(profile.Email))
			{
				_registrationNotifications.ScheduleWelcomeManualEmail(profile.Email, profile.Name, admin.DisplayName);
			}
			SetupCompleted?.Invoke(admin, EventArgs.Empty);
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is IOException || ex is ArgumentException || ex is CryptographicException || ex is PlatformNotSupportedException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void EnsureQuickLicences()
	{
		UpsertQuickLicence("20", DrugLicence20B, DrugLicence20IssueDate, DrugLicence20ExpiryDate, Licence20Label);
		UpsertQuickLicence("21", DrugLicence21B, DrugLicence21IssueDate, DrugLicence21ExpiryDate, Licence21Label);
	}

	private void UpsertQuickLicence(string licenceType, string licenceNumber, DateTime? issueDate, DateTime? expiryDate, string label)
	{
		if (!string.IsNullOrWhiteSpace(licenceNumber))
		{
			if (!issueDate.HasValue || !expiryDate.HasValue)
			{
				throw new InvalidOperationException("Enter issue date and valid-up-to date for " + label + ".");
			}
			LicenceDraftViewModel licenceDraftViewModel = Licences.FirstOrDefault((LicenceDraftViewModel item) => string.Equals(item.LicenceType, licenceType, StringComparison.OrdinalIgnoreCase));
			if (licenceDraftViewModel != null)
			{
				licenceDraftViewModel.LicenceNumber = licenceNumber.Trim();
				licenceDraftViewModel.IssueDate = issueDate;
				licenceDraftViewModel.ExpiryDate = expiryDate;
			}
			else
			{
				Licences.Add(new LicenceDraftViewModel
				{
					LicenceType = licenceType,
					LicenceNumber = licenceNumber.Trim(),
					IssueDate = issueDate,
					ExpiryDate = expiryDate
				});
			}
		}
	}

	private static string DeriveUsername(string? email)
	{
		if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
		{
			return $"admin-{Guid.NewGuid():N}".Substring(0, 12);
		}
		string text = email.Split('@')[0].Trim();
		if (text.Length < 3)
		{
			return email.Replace('@', '.');
		}
		return text;
	}

	private static string? NullIfWhiteSpace(string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedBusinessModeChanged(BusinessMode value)
	{
		OnPropertyChanged("IsRetailStoreSelected");
		OnPropertyChanged("IsWholesaleStoreSelected");
		OnPropertyChanged("IsBothStoreSelected");
		OnPropertyChanged("PageTitle");
		OnPropertyChanged("DetailsGroupHeader");
		OnPropertyChanged("NameFieldLabel");
		OnPropertyChanged("Licence20Label");
		OnPropertyChanged("Licence21Label");
		OnPropertyChanged("Licence20Placeholder");
		OnPropertyChanged("Licence21Placeholder");
	}
}
