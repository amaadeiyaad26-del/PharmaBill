using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class WholesaleCustomerPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly IFilePickerService _filePicker;

	private readonly PurchaseSpreadsheetReader _spreadsheetReader;

	private readonly CurrentSession _currentSession;

	private readonly IConfirmationService _confirmation;

	private Guid _editingCustomerId;

	private WholesaleCustomerRecord? _selectedRecord;

	private CustomerLicenceDraft? _selectedLicence;

	private string _name = string.Empty;

	private string _buyerType = "Distributor";

	private string _phone = string.Empty;

	private string _email = string.Empty;

	private string _address = string.Empty;

	private string _gstin = string.Empty;

	private string _state = string.Empty;

	private string _stateDrugControlPortalUrl = string.Empty;

	private string _creditLimit = "0.00";

	private string _creditDays = "0";

	private string _priceCategory = string.Empty;

	private string _route = string.Empty;

	private string _salesman = string.Empty;

	private string _openingBalance = "0.00";

	private bool _isActive = true;

	private string _verifiedBy = string.Empty;

	private DateTime? _verifiedOn;

	private string _verificationMethod = string.Empty;

	private string _buyerLicenceRuleType = "Distributor";

	private string _buyerLicenceRuleTypes = string.Empty;

	private string _newLicenceType = string.Empty;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? newCustomerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<WholesaleCustomerRecord?>? selectCustomerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addLicenceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<CustomerLicenceDraft?>? removeLicenceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addLicenceTypeCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveBuyerLicenceRuleCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<CustomerLicenceDraft?>? selectLicenceScanCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveCustomerCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? importExcelCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openStatePortalCommand;

	public ObservableCollection<WholesaleCustomerRecord> Customers { get; } = new ObservableCollection<WholesaleCustomerRecord>();

	public ObservableCollection<string> LicenceTypeOptions { get; } = new ObservableCollection<string>();

	public ObservableCollection<string> BuyerTypeOptions { get; } = new ObservableCollection<string> { "Distributor", "Retailer", "Hospital", "Institution", "Other" };

	public ObservableCollection<string> PatientTypeOptions { get; } = new ObservableCollection<string> { "Walk-in", "Regular", "Chronic Patient", "Staff", "Admitted/MRD" };

	private bool _isRetailMode;

	public bool IsRetailMode => _isRetailMode;

	public bool IsWholesaleMode => !_isRetailMode;

	public string PageTitle => _isRetailMode ? "Retail Customers & Patients" : "Wholesale customers";

	public string DetailsHeader => _isRetailMode ? "Customer / Patient Details" : "Buyer details";

	public string NameFieldLabel => _isRetailMode ? "Patient / Customer Name" : "Customer / legal name";

	public string TypeFieldLabel => _isRetailMode ? "Patient Type" : "Buyer type";

	public string PhoneFieldLabel => _isRetailMode ? "Mobile Phone Number" : "Phone";

	public string AddressFieldLabel => _isRetailMode ? "Address / Area" : "Address";

	public string BalanceFieldLabel => _isRetailMode ? "Running Balance (₹)" : "Opening balance (₹)";

	public string TagsFieldLabel => _isRetailMode ? "Chronic Disease / Refill Tags" : "Route";

	public string SaveButtonLabel => _isRetailMode ? "Save Customer / Patient" : "Save Customer";

	public string NewButtonLabel => _isRetailMode ? "New patient" : "New customer";

	public string ActiveFlagLabel => _isRetailMode ? "Active (clear to block credit billing)" : "Active (clear to block buyer)";

	public string RetailSuppliersHint => "Medicine distributors and wholesalers (e.g. stock creditors) are managed under Purchases → Supplier — never as retail customers.";

	public IEnumerable<string> TypeOptions => _isRetailMode ? PatientTypeOptions : BuyerTypeOptions;

	public ObservableCollection<CustomerLicenceDraft> Licences { get; } = new ObservableCollection<CustomerLicenceDraft>();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public WholesaleCustomerRecord? SelectedRecord
	{
		get
		{
			return _selectedRecord;
		}
		set
		{
			if (!EqualityComparer<WholesaleCustomerRecord>.Default.Equals(_selectedRecord, value))
			{
				OnPropertyChanging(nameof(SelectedRecord));
				_selectedRecord = value;
				OnSelectedRecordChanged(value);
				OnPropertyChanged(nameof(SelectedRecord));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public CustomerLicenceDraft? SelectedLicence
	{
		get
		{
			return _selectedLicence;
		}
		set
		{
			if (!EqualityComparer<CustomerLicenceDraft>.Default.Equals(_selectedLicence, value))
			{
				OnPropertyChanging(nameof(SelectedLicence));
				_selectedLicence = value;
				OnPropertyChanged(nameof(SelectedLicence));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Name
	{
		get
		{
			return _name;
		}
		[MemberNotNull("_name")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_name, value))
			{
				OnPropertyChanging(nameof(Name));
				_name = value;
				OnPropertyChanged(nameof(Name));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BuyerType
	{
		get
		{
			return _buyerType;
		}
		[MemberNotNull("_buyerType")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyerType, value))
			{
				OnPropertyChanging(nameof(BuyerType));
				_buyerType = value;
				OnPropertyChanged(nameof(BuyerType));
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
				OnPropertyChanging(nameof(Phone));
				_phone = value;
				OnPropertyChanged(nameof(Phone));
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
				OnPropertyChanging(nameof(Email));
				_email = value;
				OnPropertyChanged(nameof(Email));
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
				OnPropertyChanging(nameof(Address));
				_address = value;
				OnPropertyChanged(nameof(Address));
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
				OnPropertyChanging(nameof(Gstin));
				_gstin = value;
				OnPropertyChanged(nameof(Gstin));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string State
	{
		get
		{
			return _state;
		}
		[MemberNotNull("_state")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_state, value))
			{
				OnPropertyChanging(nameof(State));
				_state = value;
				OnPropertyChanged(nameof(State));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string StateDrugControlPortalUrl
	{
		get
		{
			return _stateDrugControlPortalUrl;
		}
		[MemberNotNull("_stateDrugControlPortalUrl")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_stateDrugControlPortalUrl, value))
			{
				OnPropertyChanging(nameof(StateDrugControlPortalUrl));
				_stateDrugControlPortalUrl = value;
				OnPropertyChanged(nameof(StateDrugControlPortalUrl));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CreditLimit
	{
		get
		{
			return _creditLimit;
		}
		[MemberNotNull("_creditLimit")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_creditLimit, value))
			{
				OnPropertyChanging(nameof(CreditLimit));
				_creditLimit = value;
				OnPropertyChanged(nameof(CreditLimit));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CreditDays
	{
		get
		{
			return _creditDays;
		}
		[MemberNotNull("_creditDays")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_creditDays, value))
			{
				OnPropertyChanging(nameof(CreditDays));
				_creditDays = value;
				OnPropertyChanged(nameof(CreditDays));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PriceCategory
	{
		get
		{
			return _priceCategory;
		}
		[MemberNotNull("_priceCategory")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_priceCategory, value))
			{
				OnPropertyChanging(nameof(PriceCategory));
				_priceCategory = value;
				OnPropertyChanged(nameof(PriceCategory));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Route
	{
		get
		{
			return _route;
		}
		[MemberNotNull("_route")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_route, value))
			{
				OnPropertyChanging(nameof(Route));
				_route = value;
				OnPropertyChanged(nameof(Route));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Salesman
	{
		get
		{
			return _salesman;
		}
		[MemberNotNull("_salesman")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_salesman, value))
			{
				OnPropertyChanging(nameof(Salesman));
				_salesman = value;
				OnPropertyChanged(nameof(Salesman));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string OpeningBalance
	{
		get
		{
			return _openingBalance;
		}
		[MemberNotNull("_openingBalance")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_openingBalance, value))
			{
				OnPropertyChanging(nameof(OpeningBalance));
				_openingBalance = value;
				OnPropertyChanged(nameof(OpeningBalance));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsActive
	{
		get
		{
			return _isActive;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isActive, value))
			{
				OnPropertyChanging(nameof(IsActive));
				_isActive = value;
				OnPropertyChanged(nameof(IsActive));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string VerifiedBy
	{
		get
		{
			return _verifiedBy;
		}
		[MemberNotNull("_verifiedBy")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_verifiedBy, value))
			{
				OnPropertyChanging(nameof(VerifiedBy));
				_verifiedBy = value;
				OnPropertyChanged(nameof(VerifiedBy));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? VerifiedOn
	{
		get
		{
			return _verifiedOn;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_verifiedOn, value))
			{
				OnPropertyChanging(nameof(VerifiedOn));
				_verifiedOn = value;
				OnPropertyChanged(nameof(VerifiedOn));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string VerificationMethod
	{
		get
		{
			return _verificationMethod;
		}
		[MemberNotNull("_verificationMethod")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_verificationMethod, value))
			{
				OnPropertyChanging(nameof(VerificationMethod));
				_verificationMethod = value;
				OnPropertyChanged(nameof(VerificationMethod));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BuyerLicenceRuleType
	{
		get
		{
			return _buyerLicenceRuleType;
		}
		[MemberNotNull("_buyerLicenceRuleType")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyerLicenceRuleType, value))
			{
				OnPropertyChanging(nameof(BuyerLicenceRuleType));
				_buyerLicenceRuleType = value;
				OnPropertyChanged(nameof(BuyerLicenceRuleType));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BuyerLicenceRuleTypes
	{
		get
		{
			return _buyerLicenceRuleTypes;
		}
		[MemberNotNull("_buyerLicenceRuleTypes")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyerLicenceRuleTypes, value))
			{
				OnPropertyChanging(nameof(BuyerLicenceRuleTypes));
				_buyerLicenceRuleTypes = value;
				OnPropertyChanged(nameof(BuyerLicenceRuleTypes));
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
				OnPropertyChanging(nameof(NewLicenceType));
				_newLicenceType = value;
				OnPropertyChanged(nameof(NewLicenceType));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string StatusMessage
	{
		get
		{
			return _statusMessage;
		}
		[MemberNotNull("_statusMessage")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_statusMessage, value))
			{
				OnPropertyChanging(nameof(StatusMessage));
				_statusMessage = value;
				OnPropertyChanged(nameof(StatusMessage));
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
				OnPropertyChanging(nameof(ErrorMessage));
				_errorMessage = value;
				OnPropertyChanged(nameof(ErrorMessage));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand NewCustomerCommand => newCustomerCommand ?? (newCustomerCommand = new RelayCommand(NewCustomer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<WholesaleCustomerRecord?> SelectCustomerCommand => selectCustomerCommand ?? (selectCustomerCommand = new RelayCommand<WholesaleCustomerRecord>(SelectCustomer));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddLicenceCommand => addLicenceCommand ?? (addLicenceCommand = new RelayCommand(AddLicence));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<CustomerLicenceDraft?> RemoveLicenceCommand => removeLicenceCommand ?? (removeLicenceCommand = new RelayCommand<CustomerLicenceDraft>(RemoveLicence));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddLicenceTypeCommand => addLicenceTypeCommand ?? (addLicenceTypeCommand = new RelayCommand(AddLicenceType));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveBuyerLicenceRuleCommand => saveBuyerLicenceRuleCommand ?? (saveBuyerLicenceRuleCommand = new AsyncRelayCommand(SaveBuyerLicenceRuleAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<CustomerLicenceDraft?> SelectLicenceScanCommand => selectLicenceScanCommand ?? (selectLicenceScanCommand = new RelayCommand<CustomerLicenceDraft>(SelectLicenceScan));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveCustomerCommand => saveCustomerCommand ?? (saveCustomerCommand = new AsyncRelayCommand(SaveCustomerAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ImportExcelCommand => importExcelCommand ?? (importExcelCommand = new AsyncRelayCommand(ImportExcelAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenStatePortalCommand => openStatePortalCommand ?? (openStatePortalCommand = new RelayCommand(OpenStatePortal));

	public WholesaleCustomerPageViewModel(IServiceScopeFactory scopeFactory, IFilePickerService filePicker, PurchaseSpreadsheetReader spreadsheetReader, CurrentSession currentSession, IConfirmationService confirmation)
	{
		_scopeFactory = scopeFactory;
		_filePicker = filePicker;
		_spreadsheetReader = spreadsheetReader;
		_currentSession = currentSession;
		_confirmation = confirmation;
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		BusinessMode mode = await context.PharmacyProfiles.AsNoTracking().Select((PharmacyProfile profile) => profile.BusinessMode).FirstOrDefaultAsync(cancellationToken);
		_isRetailMode = mode == BusinessMode.Retail;
		OnPropertyChanged(nameof(IsRetailMode));
		OnPropertyChanged(nameof(IsWholesaleMode));
		OnPropertyChanged(nameof(PageTitle));
		OnPropertyChanged(nameof(DetailsHeader));
		OnPropertyChanged(nameof(NameFieldLabel));
		OnPropertyChanged(nameof(TypeFieldLabel));
		OnPropertyChanged(nameof(PhoneFieldLabel));
		OnPropertyChanged(nameof(AddressFieldLabel));
		OnPropertyChanged(nameof(BalanceFieldLabel));
		OnPropertyChanged(nameof(TagsFieldLabel));
		OnPropertyChanged(nameof(SaveButtonLabel));
		OnPropertyChanged(nameof(NewButtonLabel));
		OnPropertyChanged(nameof(ActiveFlagLabel));
		OnPropertyChanged(nameof(TypeOptions));
		WholesaleCustomerService service = scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>();
		BuyerLicenceRuleTypes = ((await service.GetBuyerLicenceRulesAsync(cancellationToken)).TryGetValue(BuyerLicenceRuleType, out var value) ? string.Join(", ", value) : string.Empty);
		await ReloadCustomersAsync(service, cancellationToken);
	}

	private void NewCustomer()
	{
		_editingCustomerId = Guid.NewGuid();
		SelectedRecord = null;
		Name = string.Empty;
		BuyerType = _isRetailMode ? "Walk-in" : "Distributor";
		Phone = string.Empty;
		Email = string.Empty;
		Address = string.Empty;
		Gstin = string.Empty;
		State = string.Empty;
		StateDrugControlPortalUrl = string.Empty;
		CreditLimit = "0.00";
		CreditDays = "0";
		PriceCategory = string.Empty;
		Route = string.Empty;
		Salesman = string.Empty;
		OpeningBalance = "0.00";
		IsActive = true;
		VerifiedBy = string.Empty;
		VerifiedOn = null;
		VerificationMethod = string.Empty;
		Licences.Clear();
		ErrorMessage = string.Empty;
		StatusMessage = "New customer";
	}

	private void SelectCustomer(WholesaleCustomerRecord? record)
	{
		SelectedRecord = record;
	}

	private void AddLicence()
	{
		if (string.IsNullOrWhiteSpace(NewLicenceType))
		{
			NewLicenceType = LicenceTypeOptions.FirstOrDefault() ?? "20";
		}
		Licences.Add(new CustomerLicenceDraft
		{
			LicenceType = NewLicenceType
		});
	}

	private void RemoveLicence(CustomerLicenceDraft? licence)
	{
		if (licence != null)
		{
			Licences.Remove(licence);
		}
	}

	private void AddLicenceType()
	{
		string text = NewLicenceType.Trim();
		if (text.Length == 0)
		{
			ErrorMessage = "Enter a licence type to add it to the editable list.";
			return;
		}
		if (!LicenceTypeOptions.Contains(text, StringComparer.OrdinalIgnoreCase))
		{
			LicenceTypeOptions.Add(text);
		}
		StatusMessage = "Licence type " + text + " will be saved to the editable list.";
		ErrorMessage = string.Empty;
	}

	private async Task SaveBuyerLicenceRuleAsync()
	{
		string[] array = BuyerLicenceRuleTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (string.IsNullOrWhiteSpace(BuyerLicenceRuleType) || array.Length == 0)
		{
			ErrorMessage = "Configure one or more permitted licence types for this buyer type.";
			return;
		}
		using IServiceScope scope = _scopeFactory.CreateScope();
		await scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>().SaveBuyerLicenceRulesAsync(new Dictionary<string, IReadOnlyCollection<string>> { [BuyerLicenceRuleType.Trim()] = array });
		StatusMessage = "Allowed licence types saved for " + BuyerLicenceRuleType + ".";
		ErrorMessage = string.Empty;
	}

	private void SelectLicenceScan(CustomerLicenceDraft? licence)
	{
		if (licence == null)
		{
			return;
		}
		string text = _filePicker.PickLicenceDocument();
		if (text != null)
		{
			string text2 = Path.GetExtension(text).ToLowerInvariant();
			switch (text2)
			{
			default:
				ErrorMessage = "Licence copy must be a PDF or image file.";
				break;
			case ".pdf":
			case ".png":
			case ".jpg":
			case ".jpeg":
			case ".bmp":
			{
				string text3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "licences");
				Directory.CreateDirectory(text3);
				licence.DocumentPath = Path.Combine(text3, $"{Guid.NewGuid():N}{text2}");
				File.Copy(text, licence.DocumentPath, overwrite: false);
				ErrorMessage = string.Empty;
				break;
			}
			}
		}
	}

	private async Task SaveCustomerAsync()
	{
		ErrorMessage = string.Empty;
		try
		{
			Customer customer = BuildCustomer();
			CustomerLicence[] licences = Licences.Select((CustomerLicenceDraft item) => item.ToEntity()).ToArray();
			using IServiceScope scope = _scopeFactory.CreateScope();
			WholesaleCustomerService service = scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>();
			await service.SaveAsync(customer, licences, LicenceTypeOptions.ToArray(), (_currentSession.User ?? throw new UnauthorizedAccessException("Sign in before editing customers.")).Id);
			await ReloadCustomersAsync(service, CancellationToken.None);
			SelectedRecord = Customers.Single((WholesaleCustomerRecord item) => item.Customer.Id == customer.Id);
			StatusMessage = "Customer saved.";
		}
		catch (Exception ex) when ((ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ImportExcelAsync()
	{
		string text = _filePicker.PickCustomerExcel();
		if (text == null)
		{
			return;
		}
		ErrorMessage = string.Empty;
		try
		{
			IReadOnlyList<WholesaleCustomerImportRecord> imports = ParseCustomerRows(await _spreadsheetReader.ReadAsync(text), out IReadOnlyList<string> rowErrors);
			using IServiceScope scope = _scopeFactory.CreateScope();
			WholesaleCustomerService service = scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>();
			WholesaleCustomerImportResult result = await service.ImportAsync(imports, LicenceTypeOptions.ToArray(), (_currentSession.User ?? throw new UnauthorizedAccessException("Sign in before importing customers.")).Id);
			await ReloadCustomersAsync(service, CancellationToken.None);
			string[] array = rowErrors.Concat(result.Errors).Take(5).ToArray();
			StatusMessage = $"Imported {result.Imported}; skipped duplicates {result.SkippedDuplicates}; invalid rows {rowErrors.Count + result.Errors.Count}.";
			ErrorMessage = ((array.Length == 0) ? string.Empty : string.Join(Environment.NewLine, array));
		}
		catch (Exception ex) when ((ex is IOException || ex is InvalidDataException || ex is InvalidOperationException || ex is UnauthorizedAccessException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void OpenStatePortal()
	{
		if (!Uri.TryCreate(StateDrugControlPortalUrl, UriKind.Absolute, out Uri result) || result.Scheme != Uri.UriSchemeHttps)
		{
			ErrorMessage = "Enter the HTTPS URL of the state's drug-control portal first.";
		}
		else if (_confirmation.Confirm($"PharmaBill cannot verify this portal or the customer's licence online. Open the configured portal?\n\n{result}", "External licence portal"))
		{
			try
			{
				Process.Start(new ProcessStartInfo(result.AbsoluteUri)
				{
					UseShellExecute = true
				});
				ErrorMessage = string.Empty;
				StatusMessage = "Portal opened. Licence verification must be completed and recorded by an authorised user.";
			}
			catch (Win32Exception ex)
			{
				ErrorMessage = "The configured portal could not be opened: " + ex.Message;
			}
		}
	}

	private async Task ReloadCustomersAsync(WholesaleCustomerService service, CancellationToken cancellationToken)
	{
		Guid selectedId = _editingCustomerId;
		IReadOnlyList<string> readOnlyList = await service.GetLicenceTypesAsync(cancellationToken);
		LicenceTypeOptions.Clear();
		foreach (string item in readOnlyList)
		{
			LicenceTypeOptions.Add(item);
		}
		IReadOnlyList<WholesaleCustomerRecord> readOnlyList2 = await service.GetCustomersAsync(cancellationToken);
		Customers.Clear();
		foreach (WholesaleCustomerRecord item2 in readOnlyList2)
		{
			Customers.Add(item2);
		}
		if (selectedId != Guid.Empty)
		{
			SelectedRecord = Customers.FirstOrDefault((WholesaleCustomerRecord item) => item.Customer.Id == selectedId);
		}
	}

	private Customer BuildCustomer()
	{
		if (!decimal.TryParse(CreditLimit, NumberStyles.Number, CultureInfo.CurrentCulture, out var result) || !decimal.TryParse(OpeningBalance, NumberStyles.Number, CultureInfo.CurrentCulture, out var result2))
		{
			throw new ArgumentException("Enter valid credit limit and opening balance amounts.");
		}
		if (!int.TryParse(CreditDays, NumberStyles.Integer, CultureInfo.CurrentCulture, out var result3))
		{
			throw new ArgumentException("Enter a whole number for credit days.");
		}
		return new Customer
		{
			Id = ((_editingCustomerId == Guid.Empty) ? Guid.NewGuid() : _editingCustomerId),
			Name = Name.Trim(),
			BuyerType = BuyerType.Trim(),
			Phone = NullIfEmpty(Phone),
			Email = NullIfEmpty(Email),
			Address = NullIfEmpty(Address),
			Gstin = NullIfEmpty(Gstin),
			State = NullIfEmpty(State),
			StateDrugControlPortalUrl = NullIfEmpty(StateDrugControlPortalUrl),
			CreditLimit = result,
			CreditDays = result3,
			PriceCategory = NullIfEmpty(PriceCategory),
			Route = NullIfEmpty(Route),
			Salesman = NullIfEmpty(Salesman),
			OpeningBalance = result2,
			IsActive = IsActive,
			VerifiedBy = NullIfEmpty(VerifiedBy),
			VerifiedOnUtc = (VerifiedOn.HasValue ? new DateTime?(DateTime.SpecifyKind(VerifiedOn.Value.ToUniversalTime(), DateTimeKind.Utc)) : ((DateTime?)null)),
			VerificationMethod = NullIfEmpty(VerificationMethod)
		};
	}

	private static IReadOnlyList<WholesaleCustomerImportRecord> ParseCustomerRows(PurchaseSpreadsheetData sheet, out IReadOnlyList<string> errors)
	{
		List<string> list = new List<string>();
		Dictionary<string, WholesaleCustomerImportRecord> dictionary = new Dictionary<string, WholesaleCustomerImportRecord>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < sheet.Rows.Count; i++)
		{
			IReadOnlyDictionary<string, string> row = sheet.Rows[i];
			int value = i + 2;
			string text = Get(row, "Customer", "Customer Name", "Name");
			if (string.IsNullOrWhiteSpace(text))
			{
				list.Add($"Row {value}: customer name is required.");
				continue;
			}
			string value2 = Get(row, "Phone", "Mobile", "Contact Number");
			string value3 = Get(row, "GSTIN", "GST No", "GST Number");
			string text2 = Get(row, "Buyer Type", "Type");
			string key = ((!string.IsNullOrWhiteSpace(value3)) ? ("GST:" + Normalize(value3)) : ("NAME:" + Normalize(text) + "|PHONE:" + Normalize(value2)));
			if (!dictionary.TryGetValue(key, out var value4))
			{
				Customer customer = new Customer();
				customer.Name = text.Trim();
				customer.BuyerType = (string.IsNullOrWhiteSpace(text2) ? "Distributor" : text2.Trim());
				customer.Phone = NullIfEmpty(value2);
				customer.Email = NullIfEmpty(Get(row, "Email"));
				customer.Address = NullIfEmpty(Get(row, "Address"));
				customer.Gstin = NullIfEmpty(value3);
				customer.State = NullIfEmpty(Get(row, "State"));
				customer.StateDrugControlPortalUrl = NullIfEmpty(Get(row, "State Drug Control Portal URL", "Portal URL"));
				customer.PriceCategory = NullIfEmpty(Get(row, "Price Category"));
				customer.Route = NullIfEmpty(Get(row, "Route"));
				customer.Salesman = NullIfEmpty(Get(row, "Salesman"));
				customer.IsActive = !string.Equals(Get(row, "Status"), "Blocked", StringComparison.OrdinalIgnoreCase);
				Customer customer2 = customer;
				if (!TryOptionalDecimal(Get(row, "Credit Limit"), out var result) || !TryOptionalDecimal(Get(row, "Opening Balance"), out var result2) || !TryOptionalInt(Get(row, "Credit Days"), out var result3))
				{
					list.Add($"Row {value}: invalid credit limit, credit days or opening balance.");
					continue;
				}
				customer2.CreditLimit = result;
				customer2.OpeningBalance = result2;
				customer2.CreditDays = result3;
				value4 = new WholesaleCustomerImportRecord(customer2, Array.Empty<CustomerLicence>());
				dictionary.Add(key, value4);
			}
			string text3 = Get(row, "Licence Number", "License Number", "Licence No", "License No");
			string text4 = Get(row, "Licence Type", "License Type");
			if (!string.IsNullOrWhiteSpace(text3) || !string.IsNullOrWhiteSpace(text4))
			{
				if (string.IsNullOrWhiteSpace(text3) || string.IsNullOrWhiteSpace(text4))
				{
					list.Add($"Row {value}: licence type and number must both be supplied.");
					continue;
				}
				if (!TryDate(Get(row, "Issue Date", "Issued On"), out var result4) || !TryDate(Get(row, "Expiry Date", "Expires On"), out var result5))
				{
					list.Add($"Row {value}: invalid licence issue or expiry date.");
					continue;
				}
				CustomerLicence customerLicence = new CustomerLicence();
				customerLicence.LicenceType = text4.Trim();
				customerLicence.LicenceNumber = text3.Trim();
				customerLicence.IssuedOn = result4;
				customerLicence.ExpiresOn = result5;
				customerLicence.IssuingAuthority = NullIfEmpty(Get(row, "Issuing Authority", "Authority"));
				CustomerLicence element = customerLicence;
				dictionary[key] = value4 with
				{
					Licences = value4.Licences.Append(element).ToArray()
				};
			}
		}
		errors = list;
		return dictionary.Values.ToArray();
	}

	private static string Get(IReadOnlyDictionary<string, string> row, params string[] headers)
	{
		foreach (string key in headers)
		{
			if (row.TryGetValue(key, out string value))
			{
				return value.Trim();
			}
		}
		return string.Empty;
	}

	private static bool TryDecimal(string value, out decimal result)
	{
		if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result))
		{
			return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
		}
		return true;
	}

	private static bool TryOptionalDecimal(string value, out decimal result)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			result = 0m;
			return true;
		}
		return TryDecimal(value, out result);
	}

	private static bool TryOptionalInt(string value, out int result)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			result = 0;
			return true;
		}
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
	}

	private static bool TryDate(string value, out DateOnly? result)
	{
		result = null;
		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}
		if (DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var result2) || DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result2))
		{
			result = result2;
			return true;
		}
		return false;
	}

	private static string Normalize(string? value)
	{
		return new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
	}

	private static string? NullIfEmpty(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}

	private static string ExpiryAlertText(CustomerLicenceExpiryAlert alert)
	{
		return alert switch
		{
			CustomerLicenceExpiryAlert.Expired => "Licence expired", 
			CustomerLicenceExpiryAlert.ExpiringWithin30Days => "Licence expiry alert: within 30 days", 
			CustomerLicenceExpiryAlert.ExpiringWithin60Days => "Licence expiry alert: within 60 days", 
			_ => string.Empty, 
		};
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedRecordChanged(WholesaleCustomerRecord? value)
	{
		if ((object)value == null)
		{
			return;
		}
		_editingCustomerId = value.Customer.Id;
		Customer customer = value.Customer;
		Name = customer.Name;
		BuyerType = customer.BuyerType ?? string.Empty;
		Phone = customer.Phone ?? string.Empty;
		Email = customer.Email ?? string.Empty;
		Address = customer.Address ?? string.Empty;
		Gstin = customer.Gstin ?? string.Empty;
		State = customer.State ?? string.Empty;
		StateDrugControlPortalUrl = customer.StateDrugControlPortalUrl ?? string.Empty;
		CreditLimit = customer.CreditLimit.ToString("0.00", CultureInfo.CurrentCulture);
		CreditDays = customer.CreditDays.ToString(CultureInfo.CurrentCulture);
		PriceCategory = customer.PriceCategory ?? string.Empty;
		Route = customer.Route ?? string.Empty;
		Salesman = customer.Salesman ?? string.Empty;
		OpeningBalance = customer.OpeningBalance.ToString("0.00", CultureInfo.CurrentCulture);
		IsActive = customer.IsActive;
		VerifiedBy = customer.VerifiedBy ?? string.Empty;
		VerifiedOn = customer.VerifiedOnUtc?.ToLocalTime();
		VerificationMethod = customer.VerificationMethod ?? string.Empty;
		Licences.Clear();
		foreach (CustomerLicence licence in value.Licences)
		{
			Licences.Add(CustomerLicenceDraft.FromEntity(licence));
		}
		ErrorMessage = string.Empty;
		StatusMessage = ExpiryAlertText(value.ExpiryAlert);
	}
}
