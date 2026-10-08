using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class RetailBillingViewModel(IServiceScopeFactory scopeFactory, CurrentSession currentSession, IFilePickerService filePicker, IConfirmationService confirmationService, CatalogSearchViewModel catalogSearch, IInvoicePrintService invoicePrintService, IPrescriptionDialogService prescriptionDialogs, INavigationService navigationService, IPaymentQrDialogService paymentQrDialogs, DocumentOutputSettingsStore documentOutputSettings, SettingsPageViewModel settingsPage) : ObservableObject, ILoadablePage
{
	private string _billNumber = "Assigned on save";

	private DateTime _billDate = DateTime.Now;

	private string _patientName = string.Empty;

	private string _patientPhone = string.Empty;

	private string _patientAddress = string.Empty;

	private string _doctorName = string.Empty;

	private string _doctorRegistrationNumber = string.Empty;

	private string _patientNameError = string.Empty;

	private string _patientPhoneError = string.Empty;

	private string _patientAddressError = string.Empty;

	private string _doctorNameError = string.Empty;

	private string _doctorRegistrationError = string.Empty;

	private string _upiReference = string.Empty;

	private string _cardAuthCode = string.Empty;

	private string _chequeNumber = string.Empty;

	private string _chequeBankName = string.Empty;

	private DateTime? _chequeClearanceDate;

	private bool _creditOwnerOverride;

	private string _creditOverrideReason = string.Empty;

	private decimal _splitCashAmount;

	private string _splitSecondMethod = "UPI / QR";

	private decimal _splitSecondAmount;

	private string _creditNote = string.Empty;

	private string? _prescriptionDocumentPath;

	private Guid? _lastSavedSaleId;

	private string _prescriptionFileName = "No prescription attached";

	private bool _isScanningPrescription;

	private bool _showGeminiKeyHint;

	private string _scanStatus = "Analyzing prescription with AI...";

	private string _itemSearch = string.Empty;

	private string _errorMessage = string.Empty;

	private string _statusMessage = string.Empty;

	private string _paymentMethod = "Cash";

	private decimal _paymentAmount;

	private decimal _cashTendered;

	private bool _upiPaymentReceived;

	private string _upiId = string.Empty;

	private string _upiLink = string.Empty;

	private ImageSource? _upiQrCode;

	private string _changeDueText = string.Empty;

	private string _focusRequest = string.Empty;

	private string _focusQtyRequest = string.Empty;

	private RetailStockChoice? _selectedSearchResult;

	private RetailBillLineViewModel? _selectedBillLine;

	private HeldRetailBill? _selectedHeldBill;

	private MedicineSearchResult? _selectedOutOfStockMedicine;

	private MedicineSearchResult? _selectedSubstitute;

	private bool _isSubstituteFlyoutOpen;

	private bool _isSearching;

	private CancellationTokenSource? _itemSearchDebounce;

	private const int ItemSearchDebounceMs = 180;

	private string? _pharmacyName;

	private bool _suppressUpiAutoModal;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? searchItemsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? pickFirstSearchResultCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? confirmSelectedSubstituteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand<MedicineSearchResult?>? addFromSubstituteCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? showSubstitutesCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? closeSubstituteFlyoutCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? attachPrescriptionCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? openCameraCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? openGeminiSettingsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? focusSearchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? focusPatientCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? focusPaymentCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? newBillCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? holdBillCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<HeldRetailBill?>? resumeBillCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? cancelBillCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? removeSelectedItemCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? resumeSelectedBillCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveAndPrintCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? previewOrPickPrintFormatCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? showPaymentQrCommand;

	public ObservableCollection<RetailStockChoice> SearchResults { get; } = new ObservableCollection<RetailStockChoice>();

	public ObservableCollection<RetailBillLineViewModel> BillItems { get; } = new ObservableCollection<RetailBillLineViewModel>();

	public ObservableCollection<MedicineSearchResult> Substitutes => catalogSearch.SubstituteResults;

	public bool HasSearchResults => SearchResults.Count > 0;

	public bool HasSubstitutes => Substitutes.Count > 0;

	public IReadOnlyList<string> PaymentMethods { get; } = new _003C_003Ez__ReadOnlyArray<string>(new string[6] { "Cash", "UPI / QR", "Card / POS", "Cheque / Bank Transfer", "Credit / Ledger", "Split Payment" });

	public ObservableCollection<HeldRetailBill> HeldBills { get; } = new ObservableCollection<HeldRetailBill>();

	public IReadOnlyList<string> SplitSecondMethods { get; } = new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "UPI / QR", "Card / POS", "Credit / Ledger" });

	public decimal Subtotal => BillItems.Sum((RetailBillLineViewModel line) => line.NetAmount);

	public decimal GstAmount => BillItems.Sum((RetailBillLineViewModel line) => line.TaxAmount);

	public decimal DiscountTotal => BillItems.Sum((RetailBillLineViewModel line) => line.DiscountAmount);

	public decimal TotalAmount => BillItems.Sum((RetailBillLineViewModel line) => line.GrossAmount);

	public bool IsUpiPayment => UpiPaymentPayload.IsUpi(PaymentMethod);

	public bool HasControlledItems => BillItems.Any((RetailBillLineViewModel line) => line.RequiresPrescription);

	public bool HasPrescription => !string.IsNullOrWhiteSpace(PrescriptionDocumentPath);

	public bool IsCashPayment => UpiPaymentPayload.IsCash(PaymentMethod);

	public bool IsCardPayment => UpiPaymentPayload.IsCard(PaymentMethod);

	public bool IsChequePayment => UpiPaymentPayload.IsCheque(PaymentMethod);

	public bool IsCreditPayment => UpiPaymentPayload.IsCredit(PaymentMethod);

	public bool IsSplitPayment => UpiPaymentPayload.IsSplit(PaymentMethod);

	public bool IsPaymentAmountVisible => !IsCreditPayment;

	public string ChangeDueValue => MoneyFormat.Number(ChangeDue);

	private decimal ChangeDue { get; set; }

	public bool HasHabitFormingItems => BillItems.Any((RetailBillLineViewModel line) => line.IsHabitForming);

	public decimal SplitBalanceRemaining => RetailTaxCalculator.RoundMoney(Math.Max(0m, TotalAmount - SplitCashAmount - SplitSecondAmount));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string BillNumber
	{
		get
		{
			return _billNumber;
		}
		[MemberNotNull("_billNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_billNumber, value))
			{
				OnPropertyChanging(nameof(BillNumber));
				_billNumber = value;
				OnPropertyChanged(nameof(BillNumber));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime BillDate
	{
		get
		{
			return _billDate;
		}
		set
		{
			if (!EqualityComparer<DateTime>.Default.Equals(_billDate, value))
			{
				OnPropertyChanging(nameof(BillDate));
				_billDate = value;
				OnPropertyChanged(nameof(BillDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PatientName
	{
		get
		{
			return _patientName;
		}
		[MemberNotNull("_patientName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_patientName, value))
			{
				OnPropertyChanging(nameof(PatientName));
				_patientName = value;
				OnPatientNameChanged(value);
				OnPropertyChanged(nameof(PatientName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PatientPhone
	{
		get
		{
			return _patientPhone;
		}
		[MemberNotNull("_patientPhone")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_patientPhone, value))
			{
				OnPropertyChanging(nameof(PatientPhone));
				_patientPhone = value;
				OnPatientPhoneChanged(value);
				OnPropertyChanged(nameof(PatientPhone));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PatientAddress
	{
		get
		{
			return _patientAddress;
		}
		[MemberNotNull("_patientAddress")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_patientAddress, value))
			{
				OnPropertyChanging(nameof(PatientAddress));
				_patientAddress = value;
				OnPatientAddressChanged(value);
				OnPropertyChanged(nameof(PatientAddress));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DoctorName
	{
		get
		{
			return _doctorName;
		}
		[MemberNotNull("_doctorName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_doctorName, value))
			{
				OnPropertyChanging(nameof(DoctorName));
				_doctorName = value;
				OnDoctorNameChanged(value);
				OnPropertyChanged(nameof(DoctorName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DoctorRegistrationNumber
	{
		get
		{
			return _doctorRegistrationNumber;
		}
		[MemberNotNull("_doctorRegistrationNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_doctorRegistrationNumber, value))
			{
				OnPropertyChanging(nameof(DoctorRegistrationNumber));
				_doctorRegistrationNumber = value;
				OnDoctorRegistrationNumberChanged(value);
				OnPropertyChanged(nameof(DoctorRegistrationNumber));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PatientNameError
	{
		get
		{
			return _patientNameError;
		}
		[MemberNotNull("_patientNameError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_patientNameError, value))
			{
				OnPropertyChanging(nameof(PatientNameError));
				_patientNameError = value;
				OnPropertyChanged(nameof(PatientNameError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PatientPhoneError
	{
		get
		{
			return _patientPhoneError;
		}
		[MemberNotNull("_patientPhoneError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_patientPhoneError, value))
			{
				OnPropertyChanging(nameof(PatientPhoneError));
				_patientPhoneError = value;
				OnPropertyChanged(nameof(PatientPhoneError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PatientAddressError
	{
		get
		{
			return _patientAddressError;
		}
		[MemberNotNull("_patientAddressError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_patientAddressError, value))
			{
				OnPropertyChanging(nameof(PatientAddressError));
				_patientAddressError = value;
				OnPropertyChanged(nameof(PatientAddressError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DoctorNameError
	{
		get
		{
			return _doctorNameError;
		}
		[MemberNotNull("_doctorNameError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_doctorNameError, value))
			{
				OnPropertyChanging(nameof(DoctorNameError));
				_doctorNameError = value;
				OnPropertyChanged(nameof(DoctorNameError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string DoctorRegistrationError
	{
		get
		{
			return _doctorRegistrationError;
		}
		[MemberNotNull("_doctorRegistrationError")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_doctorRegistrationError, value))
			{
				OnPropertyChanging(nameof(DoctorRegistrationError));
				_doctorRegistrationError = value;
				OnPropertyChanged(nameof(DoctorRegistrationError));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpiReference
	{
		get
		{
			return _upiReference;
		}
		[MemberNotNull("_upiReference")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_upiReference, value))
			{
				OnPropertyChanging(nameof(UpiReference));
				_upiReference = value;
				OnPropertyChanged(nameof(UpiReference));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CardAuthCode
	{
		get
		{
			return _cardAuthCode;
		}
		[MemberNotNull("_cardAuthCode")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_cardAuthCode, value))
			{
				OnPropertyChanging(nameof(CardAuthCode));
				_cardAuthCode = value;
				OnPropertyChanged(nameof(CardAuthCode));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ChequeNumber
	{
		get
		{
			return _chequeNumber;
		}
		[MemberNotNull("_chequeNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_chequeNumber, value))
			{
				OnPropertyChanging(nameof(ChequeNumber));
				_chequeNumber = value;
				OnPropertyChanged(nameof(ChequeNumber));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ChequeBankName
	{
		get
		{
			return _chequeBankName;
		}
		[MemberNotNull("_chequeBankName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_chequeBankName, value))
			{
				OnPropertyChanging(nameof(ChequeBankName));
				_chequeBankName = value;
				OnPropertyChanged(nameof(ChequeBankName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public DateTime? ChequeClearanceDate
	{
		get
		{
			return _chequeClearanceDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_chequeClearanceDate, value))
			{
				OnPropertyChanging(nameof(ChequeClearanceDate));
				_chequeClearanceDate = value;
				OnPropertyChanged(nameof(ChequeClearanceDate));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool CreditOwnerOverride
	{
		get
		{
			return _creditOwnerOverride;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_creditOwnerOverride, value))
			{
				OnPropertyChanging(nameof(CreditOwnerOverride));
				_creditOwnerOverride = value;
				OnPropertyChanged(nameof(CreditOwnerOverride));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CreditOverrideReason
	{
		get
		{
			return _creditOverrideReason;
		}
		[MemberNotNull("_creditOverrideReason")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_creditOverrideReason, value))
			{
				OnPropertyChanging(nameof(CreditOverrideReason));
				_creditOverrideReason = value;
				OnPropertyChanged(nameof(CreditOverrideReason));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal SplitCashAmount
	{
		get
		{
			return _splitCashAmount;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_splitCashAmount, value))
			{
				OnPropertyChanging(nameof(SplitCashAmount));
				_splitCashAmount = value;
				OnSplitCashAmountChanged(value);
				OnPropertyChanged(nameof(SplitCashAmount));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SplitSecondMethod
	{
		get
		{
			return _splitSecondMethod;
		}
		[MemberNotNull("_splitSecondMethod")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_splitSecondMethod, value))
			{
				OnPropertyChanging(nameof(SplitSecondMethod));
				_splitSecondMethod = value;
				OnPropertyChanged(nameof(SplitSecondMethod));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal SplitSecondAmount
	{
		get
		{
			return _splitSecondAmount;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_splitSecondAmount, value))
			{
				OnPropertyChanging(nameof(SplitSecondAmount));
				_splitSecondAmount = value;
				OnSplitSecondAmountChanged(value);
				OnPropertyChanged(nameof(SplitSecondAmount));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CreditNote
	{
		get
		{
			return _creditNote;
		}
		[MemberNotNull("_creditNote")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_creditNote, value))
			{
				OnPropertyChanging(nameof(CreditNote));
				_creditNote = value;
				OnPropertyChanged(nameof(CreditNote));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string? PrescriptionDocumentPath
	{
		get
		{
			return _prescriptionDocumentPath;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_prescriptionDocumentPath, value))
			{
				OnPropertyChanging(nameof(PrescriptionDocumentPath));
				_prescriptionDocumentPath = value;
				OnPrescriptionDocumentPathChanged(value);
				OnPropertyChanged(nameof(PrescriptionDocumentPath));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PrescriptionFileName
	{
		get
		{
			return _prescriptionFileName;
		}
		[MemberNotNull("_prescriptionFileName")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_prescriptionFileName, value))
			{
				OnPropertyChanging(nameof(PrescriptionFileName));
				_prescriptionFileName = value;
				OnPropertyChanged(nameof(PrescriptionFileName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsScanningPrescription
	{
		get
		{
			return _isScanningPrescription;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isScanningPrescription, value))
			{
				OnPropertyChanging(nameof(IsScanningPrescription));
				_isScanningPrescription = value;
				OnPropertyChanged(nameof(IsScanningPrescription));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ShowGeminiKeyHint
	{
		get
		{
			return _showGeminiKeyHint;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_showGeminiKeyHint, value))
			{
				OnPropertyChanging(nameof(ShowGeminiKeyHint));
				_showGeminiKeyHint = value;
				OnPropertyChanged(nameof(ShowGeminiKeyHint));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ScanStatus
	{
		get
		{
			return _scanStatus;
		}
		[MemberNotNull("_scanStatus")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_scanStatus, value))
			{
				OnPropertyChanging(nameof(ScanStatus));
				_scanStatus = value;
				OnPropertyChanged(nameof(ScanStatus));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ItemSearch
	{
		get
		{
			return _itemSearch;
		}
		[MemberNotNull("_itemSearch")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_itemSearch, value))
			{
				OnPropertyChanging(nameof(ItemSearch));
				_itemSearch = value;
				OnItemSearchChanged(value);
				OnPropertyChanged(nameof(ItemSearch));
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
	public string PaymentMethod
	{
		get
		{
			return _paymentMethod;
		}
		[MemberNotNull("_paymentMethod")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_paymentMethod, value))
			{
				OnPropertyChanging(nameof(PaymentMethod));
				_paymentMethod = value;
				OnPaymentMethodChanged(value);
				OnPropertyChanged(nameof(PaymentMethod));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal PaymentAmount
	{
		get
		{
			return _paymentAmount;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_paymentAmount, value))
			{
				OnPropertyChanging(nameof(PaymentAmount));
				_paymentAmount = value;
				OnPaymentAmountChanged(value);
				OnPropertyChanged(nameof(PaymentAmount));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal CashTendered
	{
		get
		{
			return _cashTendered;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_cashTendered, value))
			{
				OnPropertyChanging(nameof(CashTendered));
				_cashTendered = value;
				OnCashTenderedChanged(value);
				OnPropertyChanged(nameof(CashTendered));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool UpiPaymentReceived
	{
		get
		{
			return _upiPaymentReceived;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_upiPaymentReceived, value))
			{
				OnPropertyChanging(nameof(UpiPaymentReceived));
				_upiPaymentReceived = value;
				OnPropertyChanged(nameof(UpiPaymentReceived));
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
				OnPropertyChanging(nameof(UpiId));
				_upiId = value;
				OnPropertyChanged(nameof(UpiId));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string UpiLink
	{
		get
		{
			return _upiLink;
		}
		[MemberNotNull("_upiLink")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_upiLink, value))
			{
				OnPropertyChanging(nameof(UpiLink));
				_upiLink = value;
				OnPropertyChanged(nameof(UpiLink));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public ImageSource? UpiQrCode
	{
		get
		{
			return _upiQrCode;
		}
		set
		{
			if (!EqualityComparer<ImageSource>.Default.Equals(_upiQrCode, value))
			{
				OnPropertyChanging(nameof(UpiQrCode));
				_upiQrCode = value;
				OnPropertyChanged(nameof(UpiQrCode));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ChangeDueText
	{
		get
		{
			return _changeDueText;
		}
		[MemberNotNull("_changeDueText")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_changeDueText, value))
			{
				OnPropertyChanging(nameof(ChangeDueText));
				_changeDueText = value;
				OnPropertyChanged(nameof(ChangeDueText));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string FocusRequest
	{
		get
		{
			return _focusRequest;
		}
		[MemberNotNull("_focusRequest")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_focusRequest, value))
			{
				OnPropertyChanging(nameof(FocusRequest));
				_focusRequest = value;
				OnPropertyChanged(nameof(FocusRequest));
			}
		}
	}

	/// <summary>Bump token to focus Qty on the selected bill line (bound to DataGrid attached behavior).</summary>
	public string FocusQtyRequest
	{
		get => _focusQtyRequest;
		private set
		{
			_focusQtyRequest = value;
			OnPropertyChanged(nameof(FocusQtyRequest));
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RetailStockChoice? SelectedSearchResult
	{
		get
		{
			return _selectedSearchResult;
		}
		set
		{
			if (!EqualityComparer<RetailStockChoice>.Default.Equals(_selectedSearchResult, value))
			{
				OnPropertyChanging(nameof(SelectedSearchResult));
				_selectedSearchResult = value;
				OnPropertyChanged(nameof(SelectedSearchResult));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public RetailBillLineViewModel? SelectedBillLine
	{
		get
		{
			return _selectedBillLine;
		}
		set
		{
			if (!EqualityComparer<RetailBillLineViewModel>.Default.Equals(_selectedBillLine, value))
			{
				OnPropertyChanging(nameof(SelectedBillLine));
				_selectedBillLine = value;
				OnPropertyChanged(nameof(SelectedBillLine));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public HeldRetailBill? SelectedHeldBill
	{
		get
		{
			return _selectedHeldBill;
		}
		set
		{
			if (!EqualityComparer<HeldRetailBill>.Default.Equals(_selectedHeldBill, value))
			{
				OnPropertyChanging(nameof(SelectedHeldBill));
				_selectedHeldBill = value;
				OnPropertyChanged(nameof(SelectedHeldBill));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public MedicineSearchResult? SelectedOutOfStockMedicine
	{
		get
		{
			return _selectedOutOfStockMedicine;
		}
		set
		{
			if (!EqualityComparer<MedicineSearchResult>.Default.Equals(_selectedOutOfStockMedicine, value))
			{
				OnPropertyChanging(nameof(SelectedOutOfStockMedicine));
				_selectedOutOfStockMedicine = value;
				OnSelectedOutOfStockMedicineChanged(value);
				OnPropertyChanged(nameof(SelectedOutOfStockMedicine));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public MedicineSearchResult? SelectedSubstitute
	{
		get
		{
			return _selectedSubstitute;
		}
		set
		{
			if (!EqualityComparer<MedicineSearchResult>.Default.Equals(_selectedSubstitute, value))
			{
				OnPropertyChanging(nameof(SelectedSubstitute));
				_selectedSubstitute = value;
				OnPropertyChanged(nameof(SelectedSubstitute));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSubstituteFlyoutOpen
	{
		get
		{
			return _isSubstituteFlyoutOpen;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSubstituteFlyoutOpen, value))
			{
				OnPropertyChanging(nameof(IsSubstituteFlyoutOpen));
				_isSubstituteFlyoutOpen = value;
				OnPropertyChanged(nameof(IsSubstituteFlyoutOpen));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsSearching
	{
		get
		{
			return _isSearching;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isSearching, value))
			{
				OnPropertyChanging(nameof(IsSearching));
				_isSearching = value;
				OnPropertyChanged(nameof(IsSearching));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SearchItemsCommand => searchItemsCommand ?? (searchItemsCommand = new AsyncRelayCommand(SearchItemsAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand PickFirstSearchResultCommand => pickFirstSearchResultCommand ?? (pickFirstSearchResultCommand = new RelayCommand(PickFirstSearchResult));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ConfirmSelectedSubstituteCommand => confirmSelectedSubstituteCommand ?? (confirmSelectedSubstituteCommand = new AsyncRelayCommand(ConfirmSelectedSubstituteAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand<MedicineSearchResult?> AddFromSubstituteCommand => addFromSubstituteCommand ?? (addFromSubstituteCommand = new AsyncRelayCommand<MedicineSearchResult>(AddFromSubstituteAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ShowSubstitutesCommand => showSubstitutesCommand ?? (showSubstitutesCommand = new AsyncRelayCommand(ShowSubstitutesAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CloseSubstituteFlyoutCommand => closeSubstituteFlyoutCommand ?? (closeSubstituteFlyoutCommand = new RelayCommand(CloseSubstituteFlyout));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AttachPrescriptionCommand => attachPrescriptionCommand ?? (attachPrescriptionCommand = new AsyncRelayCommand(AttachPrescriptionAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand OpenCameraCommand => openCameraCommand ?? (openCameraCommand = new AsyncRelayCommand(OpenCameraAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand OpenGeminiSettingsCommand => openGeminiSettingsCommand ?? (openGeminiSettingsCommand = new RelayCommand(OpenGeminiSettings));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand FocusSearchCommand => focusSearchCommand ?? (focusSearchCommand = new RelayCommand(FocusSearch));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand FocusPatientCommand => focusPatientCommand ?? (focusPatientCommand = new RelayCommand(FocusPatient));

	/// <summary>F4 — medicine / barcode search (alias of FocusSearch).</summary>
	public IRelayCommand FocusMedicineCommand => FocusSearchCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand FocusPaymentCommand => focusPaymentCommand ?? (focusPaymentCommand = new RelayCommand(FocusPayment));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand NewBillCommand => newBillCommand ?? (newBillCommand = new RelayCommand(NewBill));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand HoldBillCommand => holdBillCommand ?? (holdBillCommand = new RelayCommand(HoldBill));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<HeldRetailBill?> ResumeBillCommand => resumeBillCommand ?? (resumeBillCommand = new RelayCommand<HeldRetailBill>(ResumeBill));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand CancelBillCommand => cancelBillCommand ?? (cancelBillCommand = new RelayCommand(CancelBill));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand RemoveSelectedItemCommand => removeSelectedItemCommand ?? (removeSelectedItemCommand = new RelayCommand(RemoveSelectedItem));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand ResumeSelectedBillCommand => resumeSelectedBillCommand ?? (resumeSelectedBillCommand = new RelayCommand(ResumeSelectedBill));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveAndPrintCommand => saveAndPrintCommand ?? (saveAndPrintCommand = new AsyncRelayCommand(SaveAndPrintAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand PreviewOrPickPrintFormatCommand => previewOrPickPrintFormatCommand ?? (previewOrPickPrintFormatCommand = new AsyncRelayCommand(PreviewOrPickPrintFormatAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ShowPaymentQrCommand => showPaymentQrCommand ?? (showPaymentQrCommand = new AsyncRelayCommand(ShowPaymentQrAsync));

	public event EventHandler? BillSaved;

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmacyProfile pharmacyProfile = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		_pharmacyName = pharmacyProfile.Name;
		UpiId = pharmacyProfile.UpiId ?? string.Empty;
		BillNumber = await scope.ServiceProvider.GetRequiredService<RetailBillingService>().PreviewNextInvoiceNoAsync(cancellationToken);
	}

	private async Task DebouncedLiveSearchAsync(string value)
	{
		_itemSearchDebounce?.Cancel();
		_itemSearchDebounce?.Dispose();
		_itemSearchDebounce = new CancellationTokenSource();
		CancellationToken token = _itemSearchDebounce.Token;
		try
		{
			await Task.Delay(180, token);
			if (value.Trim().Length < 2)
			{
				SearchResults.Clear();
				OnPropertyChanged("HasSearchResults");
				return;
			}
			IsSearching = true;
			using IServiceScope scope = scopeFactory.CreateScope();
			IReadOnlyList<RetailStockChoice> readOnlyList = await scope.ServiceProvider.GetRequiredService<RetailBillingService>().SearchStockAsync(value, token);
			token.ThrowIfCancellationRequested();
			SearchResults.Clear();
			foreach (RetailStockChoice item in readOnlyList)
			{
				SearchResults.Add(item);
			}
			OnPropertyChanged("HasSearchResults");
			if (readOnlyList.Count > 0)
			{
				ClearSubstitutes();
				SelectedSearchResult = readOnlyList[0];
				return;
			}
			CatalogSearchService catalog = scope.ServiceProvider.GetRequiredService<CatalogSearchService>();
			MedicineSearchResults medicineSearchResults = await catalog.SearchAsync(value, token);
			token.ThrowIfCancellationRequested();
			MedicineSearchResult medicineSearchResult = medicineSearchResults.FromCatalog.FirstOrDefault() ?? medicineSearchResults.InStock.FirstOrDefault();
			if ((object)medicineSearchResult == null || string.IsNullOrWhiteSpace(medicineSearchResult.CompositionKey))
			{
				ClearSubstitutes();
				return;
			}
			SelectedOutOfStockMedicine = medicineSearchResult;
			List<SubstituteStockResult> list = await catalog.FindSubstitutesAsync(medicineSearchResult.CompositionKey, medicineSearchResult.CatalogMedicineId, token);
			token.ThrowIfCancellationRequested();
			catalogSearch.SubstituteResults.Clear();
			foreach (SubstituteStockResult item2 in list)
			{
				catalogSearch.SubstituteResults.Add(CatalogSearchService.ToMedicineSearchResult(item2));
			}
			OnPropertyChanged("Substitutes");
			OnPropertyChanged("HasSubstitutes");
			IsSubstituteFlyoutOpen = list.Count > 0;
			if (list.Count > 0)
			{
				SelectedSubstitute = Substitutes.FirstOrDefault((MedicineSearchResult item) => item.IsInStock) ?? Substitutes.FirstOrDefault();
				StatusMessage = "No saleable stock while typing — review salt substitutes (F5).";
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			ErrorMessage = ex2.Message;
		}
		finally
		{
			if (!token.IsCancellationRequested)
			{
				IsSearching = false;
			}
		}
	}

	private void ClearSubstitutes()
	{
		catalogSearch.SubstituteResults.Clear();
		SelectedOutOfStockMedicine = null;
		SelectedSubstitute = null;
		IsSubstituteFlyoutOpen = false;
		OnPropertyChanged("Substitutes");
		OnPropertyChanged("HasSubstitutes");
	}

	public async Task<bool> TryAddByBarcodeAsync(string barcode)
	{
		ErrorMessage = string.Empty;
		if (string.IsNullOrWhiteSpace(barcode))
		{
			return false;
		}
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			RetailStockChoice retailStockChoice = await scope.ServiceProvider.GetRequiredService<RetailBillingService>().GetByBarcodeAsync(barcode);
			if ((object)retailStockChoice == null)
			{
				return false;
			}
			AddStockChoice(retailStockChoice);
			StatusMessage = "Scanned: " + retailStockChoice.DrugName + " · batch " + retailStockChoice.BatchNo;
			return true;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
			return false;
		}
	}

	private async Task SearchItemsAsync()
	{
		_itemSearchDebounce?.Cancel();
		_itemSearchDebounce?.Dispose();
		_itemSearchDebounce = new CancellationTokenSource();
		CancellationToken token = _itemSearchDebounce.Token;
		ErrorMessage = string.Empty;
		SearchResults.Clear();
		OnPropertyChanged("HasSearchResults");
		if (string.IsNullOrWhiteSpace(ItemSearch))
		{
			IsSearching = false;
			return;
		}
		IsSearching = true;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			IReadOnlyList<RetailStockChoice> readOnlyList = await scope.ServiceProvider.GetRequiredService<RetailBillingService>().SearchStockAsync(ItemSearch, token);
			token.ThrowIfCancellationRequested();
			foreach (RetailStockChoice item in readOnlyList)
			{
				SearchResults.Add(item);
			}
			OnPropertyChanged("HasSearchResults");
			RetailStockChoice retailStockChoice = readOnlyList.FirstOrDefault((RetailStockChoice result) => string.Equals(result.Barcode, ItemSearch.Trim(), StringComparison.OrdinalIgnoreCase));
			if ((object)retailStockChoice != null)
			{
				AddStockChoice(retailStockChoice);
				return;
			}
			if (readOnlyList.Count > 0)
			{
				AddStockChoice(readOnlyList[0]);
				return;
			}
			CatalogSearchService catalog = scope.ServiceProvider.GetRequiredService<CatalogSearchService>();
			MedicineSearchResults medicineSearchResults = await catalog.SearchAsync(ItemSearch, token);
			token.ThrowIfCancellationRequested();
			SelectedOutOfStockMedicine = medicineSearchResults.FromCatalog.FirstOrDefault() ?? medicineSearchResults.InStock.FirstOrDefault();
			if ((object)SelectedOutOfStockMedicine != null)
			{
				List<SubstituteStockResult> list = await catalog.FindSubstitutesAsync(SelectedOutOfStockMedicine.CompositionKey, SelectedOutOfStockMedicine.CatalogMedicineId, token);
				catalogSearch.SubstituteResults.Clear();
				foreach (SubstituteStockResult item2 in list)
				{
					catalogSearch.SubstituteResults.Add(CatalogSearchService.ToMedicineSearchResult(item2));
				}
				OnPropertyChanged("Substitutes");
				OnPropertyChanged("HasSubstitutes");
				IsSubstituteFlyoutOpen = list.Count > 0;
				SelectedSubstitute = Substitutes.FirstOrDefault((MedicineSearchResult item) => item.IsInStock) ?? Substitutes.FirstOrDefault();
				StatusMessage = "No saleable stock found. Review substitutes; pharmacist must confirm suitability.";
				FocusRequest = "SubstituteFlyout";
			}
			else
			{
				ClearSubstitutes();
				StatusMessage = "No saleable batch found.";
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			ErrorMessage = ex2.Message;
		}
		finally
		{
			if (!token.IsCancellationRequested)
			{
				IsSearching = false;
			}
		}
	}

	private void PickFirstSearchResult()
	{
		if ((object)SelectedSearchResult != null)
		{
			AddStockChoice(SelectedSearchResult);
		}
		else if (SearchResults.Count > 0)
		{
			AddStockChoice(SearchResults[0]);
		}
	}

	private Task ConfirmSelectedSubstituteAsync()
	{
		return AddFromSubstituteAsync(SelectedSubstitute);
	}

	private async Task AddFromSubstituteAsync(MedicineSearchResult? medicine)
	{
		if ((object)medicine == null)
		{
			medicine = SelectedSubstitute;
		}
		if ((object)medicine == null)
		{
			return;
		}
		if (!medicine.IsInStock || medicine.StockQuantity <= 0m)
		{
			ErrorMessage = "Only in-stock substitutes can be added to the bill. Choose a row with stock.";
			return;
		}
		ItemSearch = medicine.Name;
		IsSubstituteFlyoutOpen = false;
		await SearchItemsAsync();
		if (SearchResults.Count == 0)
		{
			ErrorMessage = "This catalogue substitute has no saleable batch in local stock.";
		}
		else
		{
			StatusMessage = "Substituted with " + medicine.Name + ".";
		}
	}

	private async Task ShowSubstitutesAsync()
	{
		ErrorMessage = string.Empty;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			CatalogSearchService catalog = scope.ServiceProvider.GetRequiredService<CatalogSearchService>();
			MedicineSearchResult medicineSearchResult = SelectedOutOfStockMedicine;
			if ((object)medicineSearchResult == null && !string.IsNullOrWhiteSpace(ItemSearch))
			{
				MedicineSearchResults medicineSearchResults = await catalog.SearchAsync(ItemSearch.Trim());
				medicineSearchResult = (SelectedOutOfStockMedicine = medicineSearchResults.FromCatalog.FirstOrDefault() ?? medicineSearchResults.InStock.FirstOrDefault());
			}
			if ((object)medicineSearchResult == null || string.IsNullOrWhiteSpace(medicineSearchResult.CompositionKey))
			{
				ErrorMessage = "Search for a medicine (or salt) first, then press F5 for substitutes.";
				return;
			}
			List<SubstituteStockResult> list = await catalog.FindSubstitutesAsync(medicineSearchResult.CompositionKey, medicineSearchResult.CatalogMedicineId);
			catalogSearch.SubstituteResults.Clear();
			foreach (SubstituteStockResult item in list)
			{
				catalogSearch.SubstituteResults.Add(CatalogSearchService.ToMedicineSearchResult(item));
			}
			OnPropertyChanged("Substitutes");
			OnPropertyChanged("HasSubstitutes");
			IsSubstituteFlyoutOpen = list.Count > 0;
			SelectedSubstitute = Substitutes.FirstOrDefault((MedicineSearchResult item) => item.IsInStock) ?? Substitutes.FirstOrDefault();
			StatusMessage = ((list.Count == 0) ? "No salt substitutes found in the catalogue." : $"Found {list.Count} substitute brand(s). Enter or double-click an in-stock row to add.");
			FocusRequest = "SubstituteFlyout";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void CloseSubstituteFlyout()
	{
		IsSubstituteFlyoutOpen = false;
	}

	private async Task AttachPrescriptionAsync()
	{
		string text = filePicker.PickPrescriptionDocument();
		if (text != null)
		{
			await AttachAndScanAsync(text);
		}
	}

	private async Task OpenCameraAsync()
	{
		string text = prescriptionDialogs.CaptureFromWebcam();
		if (text != null)
		{
			await AttachAndScanAsync(text);
		}
	}

	private async Task AttachAndScanAsync(string path)
	{
		PrescriptionDocumentPath = path;
		PrescriptionFileName = Path.GetFileName(path);
		ErrorMessage = string.Empty;
		await ScanPrescriptionAsync(path);
	}

	private async Task ScanPrescriptionAsync(string path)
	{
		if (IsScanningPrescription)
		{
			return;
		}
		try
		{
			bool useCloud = false;
			using IServiceScope scope = scopeFactory.CreateScope();
			IPrescriptionOcrService ocr = scope.ServiceProvider.GetRequiredService<IPrescriptionOcrService>();
			if (ocr.IsCloudConfigured)
			{
				useCloud = confirmationService.Confirm("Send this prescription image to Google Gemini for higher-accuracy reading?\n\nChoose No to read it offline on this PC; the image then never leaves it.", "AI prescription reading");
			}
			ShowGeminiKeyHint = false;
			IsScanningPrescription = true;
			ScanStatus = "Analyzing prescription with AI...";
			LoadedPrescriptionImage loadedPrescriptionImage = await PrescriptionImageLoader.LoadAsync(path);
			PrescriptionParseResultDto result = await ocr.ParseAsync(loadedPrescriptionImage.Bytes, loadedPrescriptionImage.MimeType, useCloud);
			ApplyPrescriptionHeader(result);
			ShowGeminiKeyHint = result.ApiKeyMissing;
			Guid[] matchedIds = (from item in result.DetectedMedicines
				where item.MatchedCatalogProductId.HasValue
				select item.MatchedCatalogProductId.Value).Distinct().ToArray();
			Dictionary<Guid, RetailStockChoice> dictionary = ((matchedIds.Length != 0) ? (await scope.ServiceProvider.GetRequiredService<RetailBillingService>().GetFefoChoicesAsync(matchedIds)).ToDictionary((KeyValuePair<Guid, RetailStockChoice> pair) => pair.Key, (KeyValuePair<Guid, RetailStockChoice> pair) => pair.Value) : new Dictionary<Guid, RetailStockChoice>());
			Dictionary<Guid, RetailStockChoice> fefo = dictionary;
			Dictionary<Guid, string> dictionary2 = ((matchedIds.Length != 0) ? (await (from drug in scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().Drugs.AsNoTracking()
				where matchedIds.Contains(drug.Id)
				select drug).ToDictionaryAsync((Drug drug) => drug.Id, (Drug drug) => drug.Name)) : new Dictionary<Guid, string>());
			Dictionary<Guid, string> matchedNames = dictionary2;
			IsScanningPrescription = false;
			if (result.DetectedMedicines.Count == 0)
			{
				StatusMessage = (result.Message + " No medicines were detected; the prescription is attached to the bill.").Trim();
				return;
			}
			PrescriptionReviewViewModel prescriptionReviewViewModel = new PrescriptionReviewViewModel(result, fefo, matchedNames);
			IReadOnlyList<ConfirmedPrescriptionItem> readOnlyList = prescriptionDialogs.ReviewMatches(prescriptionReviewViewModel);
			if (prescriptionReviewViewModel.OpenSettingsRequested)
			{
				OpenGeminiSettings();
				return;
			}
			if (readOnlyList == null || readOnlyList.Count == 0)
			{
				StatusMessage = "No prescription medicines were added to the bill.";
				return;
			}
			foreach (ConfirmedPrescriptionItem item in readOnlyList)
			{
				AddStockChoice(item.Choice, item.Quantity);
			}
			StatusMessage = $"{readOnlyList.Count} prescription medicine(s) added to the bill. Please verify before saving.";
		}
		catch (Exception ex)
		{
			ErrorMessage = "The prescription could not be read: " + ex.Message;
		}
		finally
		{
			IsScanningPrescription = false;
		}
	}

	private void OpenGeminiSettings()
	{
		navigationService.Navigate("Settings");
	}

	private void ApplyPrescriptionHeader(PrescriptionParseResultDto result)
	{
		if (!string.IsNullOrWhiteSpace(result.PatientName))
		{
			PatientName = result.PatientName;
		}
		if (!string.IsNullOrWhiteSpace(result.DoctorName))
		{
			DoctorName = result.DoctorName;
		}
		if (!string.IsNullOrWhiteSpace(result.DoctorRegistrationNo))
		{
			DoctorRegistrationNumber = result.DoctorRegistrationNo;
		}
	}

	public void SeedItemSearch(string query)
	{
		ItemSearch = query?.Trim() ?? string.Empty;
		RequestFocus("ItemSearch");
		if (ItemSearch.Length >= 2)
		{
			SearchItemsCommand.ExecuteAsync(null);
		}
	}

	private void FocusSearch()
	{
		RequestFocus("ItemSearch");
	}

	private void FocusPatient()
	{
		RequestFocus("PatientName");
	}

	private void FocusPayment()
	{
		RequestFocus("PaymentAmount");
	}

	private void NewBill()
	{
		if (BillItems.Count <= 0 || confirmationService.Confirm("Clear the current unsaved bill?", "New bill"))
		{
			ClearBill();
		}
	}

	private void HoldBill()
	{
		if (BillItems.Count == 0)
		{
			ErrorMessage = "Add at least one item before holding a bill.";
			return;
		}
		if (HeldBills.Count >= 10)
		{
			ErrorMessage = "Up to 10 bills can be held at a time.";
			return;
		}
		HeldBills.Add(new HeldRetailBill($"{(string.IsNullOrWhiteSpace(PatientName) ? "Patient" : PatientName.Trim())} - ₹{TotalAmount:N2} ({DateTime.Now:HH:mm})", PatientName, PatientPhone, PatientAddress, DoctorName, DoctorRegistrationNumber, PrescriptionDocumentPath, BillItems.Select((RetailBillLineViewModel line) => line.ToSnapshot()).ToArray()));
		ClearBill();
		StatusMessage = $"{HeldBills.Count} bill(s) held. Resume them from the held-bill list.";
	}

	private void ResumeBill(HeldRetailBill? bill)
	{
		if ((object)bill == null)
		{
			ErrorMessage = "Select a held bill to resume.";
		}
		else
		{
			if (BillItems.Count > 0 && !confirmationService.Confirm("Hold the current bill before resuming the selected bill?", "Resume held bill"))
			{
				return;
			}
			if (BillItems.Count > 0)
			{
				if (HeldBills.Count >= 10)
				{
					ErrorMessage = "There is no room to hold the current bill. Resume after saving or removing a held bill.";
					return;
				}
				HoldBill();
			}
			HeldBills.Remove(bill);
			PatientName = bill.PatientName;
			PatientPhone = bill.PatientPhone;
			PatientAddress = bill.PatientAddress;
			DoctorName = bill.DoctorName;
			DoctorRegistrationNumber = bill.DoctorRegistrationNumber;
			PrescriptionDocumentPath = bill.PrescriptionDocumentPath;
			PrescriptionFileName = ((bill.PrescriptionDocumentPath == null) ? "No prescription attached" : Path.GetFileName(bill.PrescriptionDocumentPath));
			foreach (RetailBillLineSnapshot item in bill.Items)
			{
				RetailBillLineViewModel retailBillLineViewModel = RetailBillLineViewModel.FromSnapshot(item);
				retailBillLineViewModel.PropertyChanged += OnBillLineChanged;
				BillItems.Add(retailBillLineViewModel);
			}
			UpdateTotals();
		}
	}

	private void CancelBill()
	{
		if (BillItems.Count <= 0 || confirmationService.Confirm("Cancel the unsaved bill?", "Cancel bill"))
		{
			ClearBill();
		}
	}

	private void RemoveSelectedItem()
	{
		if (SelectedBillLine == null)
		{
			return;
		}

		if (!confirmationService.Confirm($"Remove {SelectedBillLine.DrugName} from the bill?", "Remove line"))
		{
			return;
		}

		if (SelectedBillLine != null)
		{
			BillItems.Remove(SelectedBillLine);
			SelectedBillLine = null;
			UpdateTotals();
		}
	}

	private void ResumeSelectedBill()
	{
		ResumeBill(SelectedHeldBill);
	}

	private async Task SaveAndPrintAsync()
	{
		ErrorMessage = string.Empty;
		if (currentSession.User == null)
		{
			ErrorMessage = "Sign in again before saving a bill.";
		}
		else
		{
			if (!ValidateRequiredFields())
			{
				return;
			}
			if (BillItems.Count == 0)
			{
				ErrorMessage = "Add at least one medicine to the bill.";
				return;
			}
			if (IsUpiPayment && (string.IsNullOrWhiteSpace(UpiId) || string.IsNullOrWhiteSpace(UpiLink)))
			{
				ErrorMessage = "Enter the pharmacy UPI ID in Settings and ensure the payment QR is available.";
				return;
			}
			if (IsSplitPayment && SplitBalanceRemaining > 0.009m)
			{
				ErrorMessage = $"Split payment still has ₹{SplitBalanceRemaining:N2} remaining. Adjust the cash / second leg amounts.";
				return;
			}
			if (IsCreditPayment && !CreditOwnerOverride && string.IsNullOrWhiteSpace(CreditNote))
			{
				ErrorMessage = "Credit / Ledger sales need a note or due date, or enable owner override with a reason.";
				return;
			}
			if (!IsCreditPayment || !CreditOwnerOverride || !string.IsNullOrWhiteSpace(CreditOverrideReason))
			{
				foreach (RetailBillLineViewModel billItem in BillItems)
				{
					string text = billItem.Validate();
					if (text != null)
					{
						SelectedBillLine = billItem;
						ErrorMessage = text;
						return;
					}
				}
				try
				{
					IReadOnlyList<RetailPaymentInput> payments = BuildPaymentInputs();
					using (IServiceScope scope = scopeFactory.CreateScope())
					{
						RetailBillingService service = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
						RetailSaleResult result = await service.SaveSaleAsync(new SaveRetailSaleInput(PatientName, PatientPhone, PatientAddress, DoctorName, DoctorRegistrationNumber, PrescriptionDocumentPath, BillItems.Select((RetailBillLineViewModel line) => line.ToInput()).ToArray(), payments, UpiPaymentReceived), currentSession.User.Id, currentSession.User.Role);
						BillNumber = result.InvoiceNo;
						_lastSavedSaleId = result.Sale.Id;
						ChangeDueText = ((result.ChangeDue > 0m) ? ("Change due: " + MoneyFormat.Rupees(result.ChangeDue)) : string.Empty);
						StatusMessage = $"Bill {result.InvoiceNo} saved. Total {MoneyFormat.Rupees(result.TotalAmount)}; paid {MoneyFormat.Rupees(result.PaidAmount)}.";
						string printFailure = null;
						try
						{
							await invoicePrintService.PrintAsync(result.Sale.Id);
						}
						catch (Exception ex)
						{
							printFailure = "Bill " + result.InvoiceNo + " was saved, but printing failed: " + ex.Message;
						}
						ClearBill();
						BillNumber = await service.PreviewNextInvoiceNoAsync();
						StatusMessage = "Bill " + result.InvoiceNo + " saved. Use Recent bills or Ctrl+P to reprint.";
						ErrorMessage = printFailure ?? string.Empty;
						BillSaved?.Invoke(this, EventArgs.Empty);
					}
					return;
				}
				catch (Exception ex2)
				{
					ErrorMessage = ex2.Message;
					return;
				}
			}
			ErrorMessage = "Enter an owner override reason for Credit / Ledger.";
		}
	}

	private async Task PreviewOrPickPrintFormatAsync()
	{
		ErrorMessage = string.Empty;
		Guid? lastSavedSaleId = _lastSavedSaleId;
		if (!lastSavedSaleId.HasValue)
		{
			ErrorMessage = "Save a bill with F10 first, then press Ctrl+P to preview or pick a format.";
			return;
		}
		try
		{
			await invoicePrintService.PickFormatAndPrintAsync(_lastSavedSaleId.Value);
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private bool ValidateRequiredFields()
	{
		PatientNameError = (string.IsNullOrWhiteSpace(PatientName) ? "Patient name is required" : string.Empty);
		PatientPhoneError = (string.IsNullOrWhiteSpace(PatientPhone) ? "Phone number is required" : string.Empty);
		bool hasControlledItems = HasControlledItems;
		PatientAddressError = ((hasControlledItems && string.IsNullOrWhiteSpace(PatientAddress)) ? "Address is required for H1, X and NDPS items" : string.Empty);
		DoctorNameError = ((hasControlledItems && string.IsNullOrWhiteSpace(DoctorName)) ? "Doctor name is required for H1, X and NDPS items" : string.Empty);
		DoctorRegistrationError = ((hasControlledItems && string.IsNullOrWhiteSpace(DoctorRegistrationNumber)) ? "Registration no. is required for H1, X and NDPS items" : string.Empty);
		(string, string) tuple = new (string, string)[5]
		{
			(PatientNameError, "PatientName"),
			(PatientPhoneError, "PatientPhone"),
			(PatientAddressError, "PatientAddress"),
			(DoctorNameError, "DoctorName"),
			(DoctorRegistrationError, "DoctorRegistration")
		}.FirstOrDefault(((string Error, string Target) entry) => entry.Error.Length > 0);
		if (tuple.Item2 == null)
		{
			return true;
		}
		(ErrorMessage, _) = tuple;
		RequestFocus(tuple.Item2);
		return false;
	}

	private void RequestFocus(string target)
	{
		if (string.Equals(target, "LineQty", StringComparison.Ordinal))
		{
			FocusQtyRequest = string.Empty;
			FocusQtyRequest = "qty-" + Guid.NewGuid().ToString("N");
			return;
		}

		FocusRequest = string.Empty;
		FocusRequest = target;
	}

	private IReadOnlyList<RetailPaymentInput> BuildPaymentInputs()
	{
		if (IsCreditPayment || (PaymentAmount <= 0m && !IsSplitPayment))
		{
			return Array.Empty<RetailPaymentInput>();
		}
		if (IsSplitPayment)
		{
			List<RetailPaymentInput> list = new List<RetailPaymentInput>();
			if (SplitCashAmount > 0m)
			{
				list.Add(new RetailPaymentInput("Cash", SplitCashAmount, SplitCashAmount));
			}
			if (SplitSecondAmount > 0m)
			{
				list.Add(new RetailPaymentInput(UpiPaymentPayload.NormalizeMethod(SplitSecondMethod), SplitSecondAmount, SplitSecondAmount));
			}
			return list;
		}
		string text = UpiPaymentPayload.NormalizeMethod(PaymentMethod);
		decimal num = Math.Min(PaymentAmount, TotalAmount);
		decimal tenderedAmount = ((IsCashPayment && CashTendered > 0m) ? CashTendered : num);
		if (text == "Card" && !string.IsNullOrWhiteSpace(CardAuthCode))
		{
			UpiReference = CardAuthCode.Trim();
		}
		else if (text == "Cheque")
		{
			UpiReference = string.Join(" / ", new string[3]
			{
				ChequeNumber,
				ChequeBankName,
				ChequeClearanceDate?.ToString("yyyy-MM-dd")
			}.Where((string value) => !string.IsNullOrWhiteSpace(value)));
		}
		else if (text == "Credit" && CreditOwnerOverride)
		{
			CreditNote = "OVERRIDE: " + CreditOverrideReason;
		}
		return new _003C_003Ez__ReadOnlySingleElementList<RetailPaymentInput>(new RetailPaymentInput(text, num, tenderedAmount));
	}

	private async Task ShowPaymentQrAsync()
	{
		ErrorMessage = string.Empty;
		try
		{
			using IServiceScope scope = scopeFactory.CreateScope();
			PharmacyProfile pharmacyProfile = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking().SingleAsync();
			_pharmacyName = pharmacyProfile.Name;
			UpiId = pharmacyProfile.UpiId ?? string.Empty;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
			return;
		}
		decimal num = RetailTaxCalculator.RoundMoney(Math.Min((PaymentAmount > 0m) ? PaymentAmount : TotalAmount, TotalAmount));
		if (num <= 0m)
		{
			ErrorMessage = "Add items before showing the payment QR.";
			return;
		}
		PaymentAmount = num;
		RefreshUpiLink(force: true);
		bool showCustomerQrWindow = documentOutputSettings.Load().ShowCustomerQrWindow;
		PaymentQrDialogResult paymentQrDialogResult = paymentQrDialogs.Show(new PaymentQrDialogRequest(UpiId, _pharmacyName ?? "Pharmacy", num, BillNumber, UpiQrCode, showCustomerQrWindow));
		if (paymentQrDialogResult.Outcome == PaymentQrDialogOutcome.ConfigureUpi)
		{
			settingsPage.RevealUpiConfiguration();
			navigationService.Navigate("Settings");
			StatusMessage = "Configure UPI & Digital Payment Settings, then return to Billing.";
		}
		else
		{
			if (paymentQrDialogResult.Outcome != PaymentQrDialogOutcome.Confirmed)
			{
				return;
			}
			_suppressUpiAutoModal = true;
			try
			{
				if (!IsUpiPayment)
				{
					PaymentMethod = "UPI / QR";
				}
				UpiPaymentReceived = true;
				PaymentAmount = num;
			}
			finally
			{
				_suppressUpiAutoModal = false;
			}
			await SaveAndPrintAsync();
		}
	}

	private void AddStockChoice(RetailStockChoice choice, decimal quantity = 1m)
	{
		RetailBillLineViewModel retailBillLineViewModel = BillItems.FirstOrDefault((RetailBillLineViewModel line) => line.Choice.BatchId == choice.BatchId);
		if (retailBillLineViewModel != null)
		{
			if (retailBillLineViewModel.Quantity + quantity > choice.AvailableQuantity)
			{
				ErrorMessage = $"Only {choice.AvailableQuantity} units are available in batch {choice.BatchNo}.";
				return;
			}
			retailBillLineViewModel.Quantity += quantity;
			SelectedBillLine = retailBillLineViewModel;
		}
		else
		{
			RetailBillLineViewModel retailBillLineViewModel2 = new RetailBillLineViewModel(choice)
			{
				Quantity = quantity
			};
			retailBillLineViewModel2.PropertyChanged += OnBillLineChanged;
			BillItems.Add(retailBillLineViewModel2);
			SelectedBillLine = retailBillLineViewModel2;
		}
		ErrorMessage = string.Empty;
		StatusMessage = (choice.IsHabitForming ? "Habit-forming (reference data): verify prescription and register requirements." : string.Empty);
		ItemSearch = string.Empty;
		SearchResults.Clear();
		SelectedSearchResult = null;
		OnPropertyChanged("HasSearchResults");
		OnPropertyChanged("HasControlledItems");
		OnPropertyChanged("HasHabitFormingItems");
		UpdateTotals();
		RequestFocus("LineQty");
	}

	private void OnBillLineChanged(object? sender, PropertyChangedEventArgs e)
	{
		UpdateTotals();
		if (sender is RetailBillLineViewModel retailBillLineViewModel)
		{
			string text = retailBillLineViewModel.Validate();
			if (text != null)
			{
				ErrorMessage = text;
				return;
			}
		}
		ErrorMessage = string.Empty;
	}

	private void UpdateTotals()
	{
		OnPropertyChanged("Subtotal");
		OnPropertyChanged("GstAmount");
		OnPropertyChanged("DiscountTotal");
		OnPropertyChanged("TotalAmount");
		OnPropertyChanged("HasControlledItems");
		OnPropertyChanged("HasHabitFormingItems");
		OnPropertyChanged("SplitBalanceRemaining");
		if (PaymentAmount <= 0m || PaymentAmount > TotalAmount)
		{
			PaymentAmount = TotalAmount;
		}
		if (IsSplitPayment && SplitCashAmount + SplitSecondAmount <= 0m)
		{
			SplitCashAmount = RetailTaxCalculator.RoundMoney(TotalAmount / 2m);
			SplitSecondAmount = RetailTaxCalculator.RoundMoney(TotalAmount - SplitCashAmount);
		}
		RefreshUpiLink();
		UpdateChangeDue();
	}

	private void UpdateChangeDue()
	{
		decimal num = ((CashTendered > 0m) ? CashTendered : PaymentAmount);
		decimal num2 = (ChangeDue = (IsCashPayment ? RetailTaxCalculator.RoundMoney(Math.Max(0m, num - Math.Min(PaymentAmount, TotalAmount))) : 0m));
		OnPropertyChanged("ChangeDueValue");
		ChangeDueText = ((num2 > 0m) ? ("Change due: " + MoneyFormat.Rupees(num2)) : string.Empty);
		OnPropertyChanged("SplitBalanceRemaining");
	}

	private void RefreshUpiLink(bool force = false)
	{
		decimal num = RetailTaxCalculator.RoundMoney(Math.Min((PaymentAmount > 0m) ? PaymentAmount : TotalAmount, TotalAmount));
		if ((!IsUpiPayment && !force) || string.IsNullOrWhiteSpace(UpiId) || num <= 0m)
		{
			UpiLink = string.Empty;
			UpiQrCode = null;
			return;
		}
		string text = documentOutputSettings.Load().UpiPayeeName;
		if (string.IsNullOrWhiteSpace(text))
		{
			text = _pharmacyName ?? "Pharmacy";
		}
		UpiLink = UpiPaymentPayload.Build(UpiId, text, num, BillNumber);
		UpiQrCode = QrCodeImage.FromText(UpiLink, 600);
	}

	private void ClearFieldState()
	{
		string text = (DoctorRegistrationError = string.Empty);
		string text2 = (DoctorNameError = text);
		string text4 = (PatientAddressError = text2);
		string patientNameError = (PatientPhoneError = text4);
		PatientNameError = patientNameError;
		string text7 = (CreditOverrideReason = string.Empty);
		text = (ChequeBankName = text7);
		text2 = (ChequeNumber = text);
		text4 = (CardAuthCode = text2);
		patientNameError = (CreditNote = text4);
		UpiReference = patientNameError;
		ChequeClearanceDate = null;
		CreditOwnerOverride = false;
		decimal splitCashAmount = (SplitSecondAmount = 0m);
		SplitCashAmount = splitCashAmount;
		SplitSecondMethod = "UPI / QR";
	}

	private void ClearBill()
	{
		PatientName = string.Empty;
		PatientPhone = string.Empty;
		PatientAddress = string.Empty;
		DoctorName = string.Empty;
		DoctorRegistrationNumber = string.Empty;
		PrescriptionDocumentPath = null;
		PrescriptionFileName = "No prescription attached";
		ItemSearch = string.Empty;
		SearchResults.Clear();
		OnPropertyChanged("HasSearchResults");
		BillItems.Clear();
		PaymentMethod = "Cash";
		PaymentAmount = 0m;
		CashTendered = 0m;
		UpiPaymentReceived = false;
		BillDate = DateTime.Now;
		ErrorMessage = string.Empty;
		ChangeDueText = string.Empty;
		ClearFieldState();
		UpdateTotals();
		RequestFocus("PatientName");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPatientNameChanged(string value)
	{
		PatientNameError = string.Empty;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPatientPhoneChanged(string value)
	{
		PatientPhoneError = string.Empty;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPatientAddressChanged(string value)
	{
		PatientAddressError = string.Empty;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDoctorNameChanged(string value)
	{
		DoctorNameError = string.Empty;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnDoctorRegistrationNumberChanged(string value)
	{
		DoctorRegistrationError = string.Empty;
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSplitCashAmountChanged(decimal value)
	{
		OnPropertyChanged("SplitBalanceRemaining");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSplitSecondAmountChanged(decimal value)
	{
		OnPropertyChanged("SplitBalanceRemaining");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPrescriptionDocumentPathChanged(string? value)
	{
		OnPropertyChanged("HasPrescription");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnItemSearchChanged(string value)
	{
		_itemSearchDebounce?.Cancel();
		_itemSearchDebounce?.Dispose();
		_itemSearchDebounce = null;
		if (string.IsNullOrWhiteSpace(value))
		{
			IsSearching = false;
			SearchResults.Clear();
			OnPropertyChanged("HasSearchResults");
			ClearSubstitutes();
		}
		else
		{
			DebouncedLiveSearchAsync(value);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPaymentMethodChanged(string value)
	{
		OnPropertyChanged("IsCashPayment");
		OnPropertyChanged("IsCardPayment");
		OnPropertyChanged("IsChequePayment");
		OnPropertyChanged("IsCreditPayment");
		OnPropertyChanged("IsSplitPayment");
		OnPropertyChanged("IsPaymentAmountVisible");
		UpdateChangeDue();
		OnPropertyChanged("IsUpiPayment");
		RefreshUpiLink();
		if (!_suppressUpiAutoModal && IsUpiPayment && TotalAmount > 0m)
		{
			ShowPaymentQrAsync();
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPaymentAmountChanged(decimal value)
	{
		RefreshUpiLink();
		UpdateChangeDue();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCashTenderedChanged(decimal value)
	{
		UpdateChangeDue();
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedOutOfStockMedicineChanged(MedicineSearchResult? value)
	{
		OnPropertyChanged("Substitutes");
		OnPropertyChanged("HasSubstitutes");
	}
}
