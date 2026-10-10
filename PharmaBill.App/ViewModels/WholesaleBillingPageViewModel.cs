using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Data.Services.Dunning;

namespace PharmaBill.App.ViewModels;

public class WholesaleBillingPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _session;

	private readonly IConfirmationService _confirmation;

	private readonly WholesaleInvoiceDocumentService _documents;

	private readonly IPaymentQrDialogService _paymentQrDialogs;

	private readonly DocumentOutputSettingsStore _documentOutputSettings;

	private readonly INavigationService _navigation;

	private readonly SettingsPageViewModel _settingsPage;

	private readonly IQuickAddMedicineDialog _quickAdd;

	private readonly ISmartDrugLookupService _smartLookup;

	private string _pharmacyName = "Pharmacy";

	private string _upiId = string.Empty;

	private bool _suppressUpiAutoModal;

	private Customer? _selectedCustomer;

	private WholesaleStockChoice? _selectedStockToAdd;

	private WholesaleInvoiceLineDraft? _selectedLine;

	private string _invoiceNoPreview = string.Empty;

	private bool _isStockChoicesLoading;

	private string _paymentMethod = "Cash";

	private decimal _paidAmount;

	private string _paymentReference = string.Empty;

	private bool _confirmNearExpiry;

	private bool _overrideCreditOrOverdue;

	private string _overrideReason = string.Empty;

	private string _transportDetails = string.Empty;

	private string _vehicleNumber = string.Empty;

	private string _eWayBillNumber = string.Empty;

	private string _irn = string.Empty;

	private string _notes = string.Empty;

	private string _statusMessage = string.Empty;

	private string _errorMessage = string.Empty;

	private Guid? _lastInvoiceId;

	private string _creditLockAlert = string.Empty;

	private string _customerSearchText = string.Empty;

	private bool _isCustomerDropDownOpen;

	private bool _suppressCustomerSearchSync;

	private string _medicineSearchText = string.Empty;

	private bool _isMedicineDropDownOpen;

	private bool _suppressMedicineSearchSync;

	private WholesaleMedicinePickerItem? _selectedMedicineItem;

	private string _focusQtyRequest = string.Empty;

	private string _lineFocusField = "Qty";

	private string _focusRequest = string.Empty;

	private RelayCommand? newBillCommand;

	private RelayCommand? focusCustomerCommand;

	private RelayCommand? focusMedicineCommand;

	private RelayCommand? focusPaymentCommand;

	private RelayCommand? removeSelectedLineCommand;

	private CancellationTokenSource? _medicineSearchCts;

	private string _buyerDrugLicence = string.Empty;

	private string _buyerGstin = string.Empty;

	private string _buyerPhone = string.Empty;

	private string _buyerAddress = string.Empty;

	private int _buyerCreditDays;

	private string _buyerDueDateDisplay = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? showPaymentQrCommand;

	private AsyncRelayCommand? addNewRetailerCommand;

	private AsyncRelayCommand? quickAddMedicineCommand;

	private AsyncRelayCommand? searchOnlineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? addLineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<WholesaleInvoiceLineDraft?>? removeLineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveInvoiceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveAndPrintCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportTaxInvoiceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportDeliveryChallanCommand;

	public string[] PaymentMethods { get; } = new string[5] { "Cash", "UPI / QR", "Card / POS", "Cheque / Bank Transfer", "Credit / Ledger" };

	public string[] SaleUnits { get; } = new string[3] { "Unit", "Strip", "Box" };

	public bool IsUpiPayment => UpiPaymentPayload.IsUpi(PaymentMethod);

	public ObservableCollection<Customer> Customers { get; } = new ObservableCollection<Customer>();

	public ObservableCollection<Customer> FilteredCustomers { get; } = new ObservableCollection<Customer>();

	public ObservableCollection<WholesaleStockChoice> StockChoices { get; } = new ObservableCollection<WholesaleStockChoice>();

	public ObservableCollection<WholesaleMedicinePickerItem> MedicinePickerItems { get; } = new ObservableCollection<WholesaleMedicinePickerItem>();

	public ObservableCollection<WholesaleInvoiceLineDraft> Items { get; } = new ObservableCollection<WholesaleInvoiceLineDraft>();

	public bool HasCreditLockAlert => !string.IsNullOrWhiteSpace(CreditLockAlert);

	public bool HasStockChoices => StockChoices.Count > 0;

	public bool HasMedicinePickerItems => MedicinePickerItems.Count > 0;

	public bool ShowMedicineSearchPlaceholder => string.IsNullOrWhiteSpace(MedicineSearchText) && SelectedMedicineItem == null;

	public bool ShowNamedQuickAdd
	{
		get
		{
			string term = MedicineSearchText.Trim();
			if (term.Length == 0)
			{
				return true;
			}

			if (term.Length < 2)
			{
				return false;
			}

			return MedicinePickerItems.Count == 0 || MedicinePickerItems.All(item => item.IsQuickAdd);
		}
	}

	public string AddNamedMedicineCaption => "+ New Medicine";

	public bool ShowCreateUnlisted
	{
		get
		{
			string term = MedicineSearchText.Trim();
			return term.Length >= 2 && (MedicinePickerItems.Count == 0 || MedicinePickerItems.All(item => item.IsQuickAdd));
		}
	}

	public string CreateUnlistedCaption => "+ Create '" + MedicineSearchText.Trim() + "' as New Medicine";

	public bool ShowOnlineLookup =>
		MedicineSearchText.Trim().Length >= 2
		&& MedicinePickerItems.Count > 0
		&& MedicinePickerItems.All(item => item.IsQuickAdd);

	public string OnlineLookupCaption => SmartDrugLookupPrompt.Caption;

	public bool HasSelectedCustomer => SelectedCustomer != null;

	public bool ShowRetailerSearchPlaceholder => SelectedCustomer == null && string.IsNullOrWhiteSpace(CustomerSearchText);

	public bool ShowAddNewRetailerOption
	{
		get
		{
			string needle = CustomerSearchText?.Trim() ?? string.Empty;
			if (needle.Length < 2)
			{
				return false;
			}

			return !Customers.Any(c => string.Equals(c.Name?.Trim(), needle, StringComparison.OrdinalIgnoreCase));
		}
	}

	public string AddNewRetailerCaption
	{
		get
		{
			string needle = CustomerSearchText?.Trim() ?? string.Empty;
			if (needle.Length == 0)
			{
				return "+ Add New Retailer";
			}

			string shortName = needle.Length > 28 ? needle[..28] + "…" : needle;
			return $"+ Add '{shortName}' as New Retailer";
		}
	}

	public string StockPickerPlaceholder
	{
		get
		{
			if (!IsStockChoicesLoading)
			{
				if (!HasMedicinePickerItems && string.IsNullOrWhiteSpace(MedicineSearchText))
				{
					return "Search medicine name, brand, or molecule…";
				}
				return "Search or select medicine / batch…";
			}
			return "Loading stocked batches…";
		}
	}

	public bool ShowStockPickerPlaceholder => ShowMedicineSearchPlaceholder;

	public string MedicineSearchText
	{
		get => _medicineSearchText;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_medicineSearchText, value))
			{
				OnPropertyChanging(nameof(MedicineSearchText));
				_medicineSearchText = value ?? string.Empty;
				OnPropertyChanged(nameof(MedicineSearchText));
				OnPropertyChanged(nameof(ShowMedicineSearchPlaceholder));
				OnPropertyChanged(nameof(ShowStockPickerPlaceholder));
				OnPropertyChanged(nameof(StockPickerPlaceholder));
				OnPropertyChanged(nameof(ShowNamedQuickAdd));
				OnPropertyChanged(nameof(AddNamedMedicineCaption));
				OnPropertyChanged(nameof(ShowCreateUnlisted));
				OnPropertyChanged(nameof(CreateUnlistedCaption));
				OnPropertyChanged(nameof(ShowOnlineLookup));
				bool highlightOnly = SelectedMedicineItem != null
					&& string.Equals(_medicineSearchText, SelectedMedicineItem.DisplayTitle, StringComparison.Ordinal);
				if (!_suppressMedicineSearchSync && !highlightOnly)
				{
					_ = DebouncedMedicineSearchAsync();
				}
			}
		}
	}

	public bool IsMedicineDropDownOpen
	{
		get => _isMedicineDropDownOpen;
		set
		{
			if (_isMedicineDropDownOpen != value)
			{
				OnPropertyChanging(nameof(IsMedicineDropDownOpen));
				_isMedicineDropDownOpen = value;
				OnPropertyChanged(nameof(IsMedicineDropDownOpen));
			}
		}
	}

	public WholesaleMedicinePickerItem? SelectedMedicineItem
	{
		get => _selectedMedicineItem;
		set
		{
			if (!EqualityComparer<WholesaleMedicinePickerItem>.Default.Equals(_selectedMedicineItem, value))
			{
				OnPropertyChanging(nameof(SelectedMedicineItem));
				_selectedMedicineItem = value;
				OnPropertyChanged(nameof(SelectedMedicineItem));
				OnPropertyChanged(nameof(ShowMedicineSearchPlaceholder));
				OnPropertyChanged(nameof(ShowStockPickerPlaceholder));
			}
		}
	}

	private bool _explicitMedicinePick;

	public void NoteExplicitMedicinePick() => _explicitMedicinePick = true;

	public WholesaleMedicinePickerItem? TakeEnterPick()
	{
		if (_explicitMedicinePick && SelectedMedicineItem != null)
		{
			return SelectedMedicineItem;
		}

		return OldestValidPick() ?? SelectedMedicineItem ?? MedicinePickerItems.FirstOrDefault();
	}

	private WholesaleMedicinePickerItem? OldestValidPick()
	{
		List<WholesaleMedicinePickerItem> inStock = MedicinePickerItems.Where(item => item.IsInStock && item.StockChoice != null).ToList();
		if (inStock.Count == 0)
		{
			return null;
		}

		string needle = MedicineSearchText.Trim();
		List<WholesaleMedicinePickerItem> named = needle.Length == 0
			? inStock
			: inStock.Where(item => item.DisplayTitle.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
		List<WholesaleMedicinePickerItem> pool = named.Count > 0 ? named : inStock;
		Guid? drugId = pool.OrderBy(item => item.DisplayTitle, StringComparer.OrdinalIgnoreCase).First().DrugId;
		return pool.Where(item => item.DrugId == drugId)
			.OrderBy(item => item.StockChoice!.ExpiryDate ?? DateOnly.MaxValue)
			.ThenBy(item => item.StockChoice!.BatchNo, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
	}

	public void CommitMedicinePick(WholesaleMedicinePickerItem? item)
	{
		if (item == null)
		{
			return;
		}

		if (!EqualityComparer<WholesaleMedicinePickerItem>.Default.Equals(_selectedMedicineItem, item))
		{
			SelectedMedicineItem = item;
		}

		_ = OnMedicineItemSelectedAsync(item);
	}

	public decimal Subtotal => Items.Sum((WholesaleInvoiceLineDraft item) => item.LineAmount);

	public decimal EstimatedTax => Items.Sum((WholesaleInvoiceLineDraft item) => decimal.Round(item.LineAmount * (item.StockChoice?.GstRate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero));

	public decimal EstimatedTotal => decimal.Round(Subtotal + EstimatedTax, 0, MidpointRounding.AwayFromZero);

	/// <summary>Bump token to focus Qty on the selected invoice line.</summary>
	public string FocusQtyRequest
	{
		get => _focusQtyRequest;
		private set
		{
			_focusQtyRequest = value;
			OnPropertyChanged(nameof(FocusQtyRequest));
		}
	}

	/// <summary>Bump token to focus a named wholesale control (CustomerSearch / MedicineSearch / Payment).</summary>
	public string FocusRequest
	{
		get => _focusRequest;
		private set
		{
			_focusRequest = value;
			OnPropertyChanged(nameof(FocusRequest));
		}
	}

	public string CustomerSearchText
	{
		get => _customerSearchText;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_customerSearchText, value))
			{
				OnPropertyChanging(nameof(CustomerSearchText));
				_customerSearchText = value ?? string.Empty;
				OnPropertyChanged(nameof(CustomerSearchText));
				OnPropertyChanged(nameof(ShowRetailerSearchPlaceholder));
				OnPropertyChanged(nameof(ShowAddNewRetailerOption));
				OnPropertyChanged(nameof(AddNewRetailerCaption));
				if (!_suppressCustomerSearchSync)
				{
					ApplyCustomerFilter(openDropDown: true);
					TrySelectExactCustomerMatch();
				}
			}
		}
	}

	public bool IsCustomerDropDownOpen
	{
		get => _isCustomerDropDownOpen;
		set
		{
			if (_isCustomerDropDownOpen != value)
			{
				OnPropertyChanging(nameof(IsCustomerDropDownOpen));
				_isCustomerDropDownOpen = value;
				OnPropertyChanged(nameof(IsCustomerDropDownOpen));
			}
		}
	}

	public string BuyerDrugLicence
	{
		get => _buyerDrugLicence;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyerDrugLicence, value))
			{
				OnPropertyChanging(nameof(BuyerDrugLicence));
				_buyerDrugLicence = value;
				OnPropertyChanged(nameof(BuyerDrugLicence));
			}
		}
	}

	public string BuyerGstin
	{
		get => _buyerGstin;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyerGstin, value))
			{
				OnPropertyChanging(nameof(BuyerGstin));
				_buyerGstin = value;
				OnPropertyChanged(nameof(BuyerGstin));
			}
		}
	}

	public string BuyerPhone
	{
		get => _buyerPhone;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyerPhone, value))
			{
				OnPropertyChanging(nameof(BuyerPhone));
				_buyerPhone = value;
				OnPropertyChanged(nameof(BuyerPhone));
			}
		}
	}

	public string BuyerAddress
	{
		get => _buyerAddress;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyerAddress, value))
			{
				OnPropertyChanging(nameof(BuyerAddress));
				_buyerAddress = value;
				OnPropertyChanged(nameof(BuyerAddress));
			}
		}
	}

	public int BuyerCreditDays
	{
		get => _buyerCreditDays;
		private set
		{
			if (_buyerCreditDays != value)
			{
				OnPropertyChanging(nameof(BuyerCreditDays));
				_buyerCreditDays = value;
				OnPropertyChanged(nameof(BuyerCreditDays));
			}
		}
	}

	public string BuyerDueDateDisplay
	{
		get => _buyerDueDateDisplay;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_buyerDueDateDisplay, value))
			{
				OnPropertyChanging(nameof(BuyerDueDateDisplay));
				_buyerDueDateDisplay = value;
				OnPropertyChanged(nameof(BuyerDueDateDisplay));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Customer? SelectedCustomer
	{
		get
		{
			return _selectedCustomer;
		}
		set
		{
			if (!EqualityComparer<Customer>.Default.Equals(_selectedCustomer, value))
			{
				OnPropertyChanging(nameof(SelectedCustomer));
				_selectedCustomer = value;
				OnSelectedCustomerChanged(value);
				OnPropertyChanged(nameof(SelectedCustomer));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public WholesaleStockChoice? SelectedStockToAdd
	{
		get
		{
			return _selectedStockToAdd;
		}
		set
		{
			if (!EqualityComparer<WholesaleStockChoice>.Default.Equals(_selectedStockToAdd, value))
			{
				OnPropertyChanging(nameof(SelectedStockToAdd));
				_selectedStockToAdd = value;
				OnSelectedStockToAddChanged(value);
				OnPropertyChanged(nameof(SelectedStockToAdd));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public WholesaleInvoiceLineDraft? SelectedLine
	{
		get
		{
			return _selectedLine;
		}
		set
		{
			if (!EqualityComparer<WholesaleInvoiceLineDraft>.Default.Equals(_selectedLine, value))
			{
				OnPropertyChanging(nameof(SelectedLine));
				_selectedLine = value;
				OnPropertyChanged(nameof(SelectedLine));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string InvoiceNoPreview
	{
		get
		{
			return _invoiceNoPreview;
		}
		[MemberNotNull("_invoiceNoPreview")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_invoiceNoPreview, value))
			{
				OnPropertyChanging(nameof(InvoiceNoPreview));
				_invoiceNoPreview = value;
				OnPropertyChanged(nameof(InvoiceNoPreview));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool IsStockChoicesLoading
	{
		get
		{
			return _isStockChoicesLoading;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_isStockChoicesLoading, value))
			{
				OnPropertyChanging(nameof(IsStockChoicesLoading));
				_isStockChoicesLoading = value;
				OnPropertyChanged(nameof(IsStockChoicesLoading));
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
	public decimal PaidAmount
	{
		get
		{
			return _paidAmount;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_paidAmount, value))
			{
				OnPropertyChanging(nameof(PaidAmount));
				_paidAmount = value;
				OnPropertyChanged(nameof(PaidAmount));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string PaymentReference
	{
		get
		{
			return _paymentReference;
		}
		[MemberNotNull("_paymentReference")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_paymentReference, value))
			{
				OnPropertyChanging(nameof(PaymentReference));
				_paymentReference = value;
				OnPropertyChanged(nameof(PaymentReference));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool ConfirmNearExpiry
	{
		get
		{
			return _confirmNearExpiry;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_confirmNearExpiry, value))
			{
				OnPropertyChanging(nameof(ConfirmNearExpiry));
				_confirmNearExpiry = value;
				OnPropertyChanged(nameof(ConfirmNearExpiry));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool OverrideCreditOrOverdue
	{
		get
		{
			return _overrideCreditOrOverdue;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_overrideCreditOrOverdue, value))
			{
				OnPropertyChanging(nameof(OverrideCreditOrOverdue));
				_overrideCreditOrOverdue = value;
				OnPropertyChanged(nameof(OverrideCreditOrOverdue));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string OverrideReason
	{
		get
		{
			return _overrideReason;
		}
		[MemberNotNull("_overrideReason")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_overrideReason, value))
			{
				OnPropertyChanging(nameof(OverrideReason));
				_overrideReason = value;
				OnPropertyChanged(nameof(OverrideReason));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string TransportDetails
	{
		get
		{
			return _transportDetails;
		}
		[MemberNotNull("_transportDetails")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_transportDetails, value))
			{
				OnPropertyChanging(nameof(TransportDetails));
				_transportDetails = value;
				OnPropertyChanged(nameof(TransportDetails));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string VehicleNumber
	{
		get
		{
			return _vehicleNumber;
		}
		[MemberNotNull("_vehicleNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_vehicleNumber, value))
			{
				OnPropertyChanging(nameof(VehicleNumber));
				_vehicleNumber = value;
				OnPropertyChanged(nameof(VehicleNumber));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string EWayBillNumber
	{
		get
		{
			return _eWayBillNumber;
		}
		[MemberNotNull("_eWayBillNumber")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_eWayBillNumber, value))
			{
				OnPropertyChanging(nameof(EWayBillNumber));
				_eWayBillNumber = value;
				OnPropertyChanged(nameof(EWayBillNumber));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Irn
	{
		get
		{
			return _irn;
		}
		[MemberNotNull("_irn")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_irn, value))
			{
				OnPropertyChanging(nameof(Irn));
				_irn = value;
				OnPropertyChanged(nameof(Irn));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string Notes
	{
		get
		{
			return _notes;
		}
		[MemberNotNull("_notes")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_notes, value))
			{
				OnPropertyChanging(nameof(Notes));
				_notes = value;
				OnPropertyChanged(nameof(Notes));
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Guid? LastInvoiceId
	{
		get
		{
			return _lastInvoiceId;
		}
		set
		{
			if (!EqualityComparer<Guid?>.Default.Equals(_lastInvoiceId, value))
			{
				OnPropertyChanging(nameof(LastInvoiceId));
				_lastInvoiceId = value;
				OnPropertyChanged(nameof(LastInvoiceId));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string CreditLockAlert
	{
		get
		{
			return _creditLockAlert;
		}
		[MemberNotNull("_creditLockAlert")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_creditLockAlert, value))
			{
				OnPropertyChanging(nameof(CreditLockAlert));
				_creditLockAlert = value;
				OnCreditLockAlertChanged(value);
				OnPropertyChanged(nameof(CreditLockAlert));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ShowPaymentQrCommand => showPaymentQrCommand ?? (showPaymentQrCommand = new AsyncRelayCommand(ShowPaymentQrAsync));

	public IAsyncRelayCommand AddNewRetailerCommand => addNewRetailerCommand ?? (addNewRetailerCommand = new AsyncRelayCommand(AddNewRetailerAsync));

	public IAsyncRelayCommand QuickAddMedicineCommand => quickAddMedicineCommand ?? (quickAddMedicineCommand = new AsyncRelayCommand(() => QuickAddMedicineAsync(MedicineSearchText)));

	public IAsyncRelayCommand SearchOnlineCommand => searchOnlineCommand ?? (searchOnlineCommand = new AsyncRelayCommand(SearchOnlineAndInwardAsync));

	public IAsyncRelayCommand ExtractOrderDocumentsCommand => extractOrderDocumentsCommand ?? (extractOrderDocumentsCommand = new AsyncRelayCommand(ExtractOrderDocumentsAsync));

	private IAsyncRelayCommand? extractOrderDocumentsCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand AddLineCommand => addLineCommand ?? (addLineCommand = new AsyncRelayCommand(AddLineAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<WholesaleInvoiceLineDraft?> RemoveLineCommand => removeLineCommand ?? (removeLineCommand = new RelayCommand<WholesaleInvoiceLineDraft>(RemoveLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveInvoiceCommand => saveInvoiceCommand ?? (saveInvoiceCommand = new AsyncRelayCommand(SaveInvoiceAsync));

	public IAsyncRelayCommand SaveAndPrintCommand => saveAndPrintCommand ?? (saveAndPrintCommand = new AsyncRelayCommand(SaveAndPrintAsync));

	public IRelayCommand NewBillCommand => newBillCommand ?? (newBillCommand = new RelayCommand(NewBill));

	public IRelayCommand FocusCustomerCommand => focusCustomerCommand ?? (focusCustomerCommand = new RelayCommand(FocusCustomer));

	public IRelayCommand FocusMedicineCommand => focusMedicineCommand ?? (focusMedicineCommand = new RelayCommand(FocusMedicine));

	public IRelayCommand FocusPaymentCommand => focusPaymentCommand ?? (focusPaymentCommand = new RelayCommand(FocusPayment));

	public IRelayCommand FocusPatientCommand => FocusCustomerCommand;

	public IRelayCommand FocusSearchCommand => FocusMedicineCommand;

	public IRelayCommand FocusMedicineSearchCommand => FocusMedicineCommand;

	private RelayCommand? holdBillCommand;

	private RelayCommand? cancelBillCommand;

	private RelayCommand? showSubstitutesCommand;

	/// <summary>F8 / Ctrl+H — wholesale has no hold queue; prompts to save or start a new invoice.</summary>
	public IRelayCommand HoldBillCommand => holdBillCommand ??= new RelayCommand(() =>
	{
		StatusMessage = "Wholesale bills are not parked — use Save & Print (F10) or New Invoice (F2).";
	});

	/// <summary>Esc / Ctrl+W — clear the active wholesale invoice draft.</summary>
	public IRelayCommand CancelBillCommand => cancelBillCommand ??= new RelayCommand(NewBill);

	/// <summary>F5 substitutes are retail-oriented; keep binding safe on wholesale.</summary>
	public IRelayCommand ShowSubstitutesCommand => showSubstitutesCommand ??= new RelayCommand(() =>
	{
		StatusMessage = "Salt substitutes are available on the Retail Counter desk.";
	});

	public IRelayCommand RemoveSelectedLineCommand => removeSelectedLineCommand ?? (removeSelectedLineCommand = new RelayCommand(RemoveSelectedLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportTaxInvoiceCommand => exportTaxInvoiceCommand ?? (exportTaxInvoiceCommand = new AsyncRelayCommand(ExportTaxInvoiceAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportDeliveryChallanCommand => exportDeliveryChallanCommand ?? (exportDeliveryChallanCommand = new AsyncRelayCommand(ExportDeliveryChallanAsync));

	public WholesaleBillingPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session, IConfirmationService confirmation, WholesaleInvoiceDocumentService documents, IPaymentQrDialogService paymentQrDialogs, DocumentOutputSettingsStore documentOutputSettings, INavigationService navigation, SettingsPageViewModel settingsPage, IQuickAddMedicineDialog quickAdd, ISmartDrugLookupService smartLookup)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_confirmation = confirmation;
		_documents = documents;
		_paymentQrDialogs = paymentQrDialogs;
		_documentOutputSettings = documentOutputSettings;
		_navigation = navigation;
		_settingsPage = settingsPage;
		_quickAdd = quickAdd;
		_smartLookup = smartLookup;
		Items.CollectionChanged += OnItemsChanged;
	}

	private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.OldItems != null)
		{
			foreach (WholesaleInvoiceLineDraft oldItem in e.OldItems)
			{
				oldItem.PropertyChanged -= OnLinePropertyChanged;
			}
		}
		if (e.NewItems != null)
		{
			foreach (WholesaleInvoiceLineDraft newItem in e.NewItems)
			{
				newItem.PropertyChanged += OnLinePropertyChanged;
			}
		}
		NotifyTotalsChanged();
	}

	private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		NotifyTotalsChanged();
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		IsStockChoicesLoading = true;
		OnPropertyChanged("StockPickerPlaceholder");
		OnPropertyChanged("HasStockChoices");
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			List<Customer> list = await (from customer in context.Customers.AsNoTracking()
				where customer.IsActive
				orderby customer.Name
				select customer).ToListAsync(cancellationToken);
			Customers.Clear();
			foreach (Customer item in list)
			{
				Customers.Add(item);
			}
			ApplyCustomerFilter();
			List<Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
				where drug.IsActive
				select drug).ToListAsync(cancellationToken);
			Guid[] drugIds = drugs.Select((Drug drug) => drug.Id).ToArray();
			List<Batch> batches = await (from batch in context.Batches.AsNoTracking()
				where drugIds.Contains(batch.DrugId)
				select batch).ToListAsync(cancellationToken);
			Guid[] batchIds = batches.Select((Batch batch) => batch.Id).ToArray();
			List<DrugPackLevel> packLevels = await (from pack in context.DrugPackLevels.AsNoTracking()
				where drugIds.Contains(pack.DrugId)
				select pack).ToListAsync(cancellationToken);
			Dictionary<Guid, List<DrugPackLevel>> packsByDrug = packLevels.GroupBy(p => p.DrugId).ToDictionary(g => g.Key, g => g.ToList());
			Dictionary<Guid, decimal> stock = await (from movement in context.StockMovements.AsNoTracking()
				where batchIds.Contains(movement.BatchId)
				group movement by movement.BatchId into @group
				select new
				{
					BatchId = @group.Key,
					Quantity = @group.Sum((StockMovement movement) => movement.QuantityChange)
				}).ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
			List<WholesaleStockChoice> list2 = (from batch in batches
				join drug in drugs on batch.DrugId equals drug.Id
				let available = stock.GetValueOrDefault(batch.Id)
				where available > 0m && (!batch.ExpiryDate.HasValue || batch.ExpiryDate.Value >= DateOnly.FromDateTime(DateTime.Today))
				orderby batch.ExpiryDate ?? DateOnly.MaxValue, drug.Name
				select BuildStockChoice(drug, batch, available, packsByDrug.GetValueOrDefault(drug.Id))).ToList();
			StockChoices.Clear();
			foreach (WholesaleStockChoice item2 in list2)
			{
				StockChoices.Add(item2);
			}
			if ((object)SelectedStockToAdd == null)
			{
				SelectedStockToAdd = StockChoices.FirstOrDefault();
			}
			OnPropertyChanged("HasStockChoices");
			OnPropertyChanged("StockPickerPlaceholder");
			SeedMedicinePickerFromStock();
			NumberSeriesService series = scope.ServiceProvider.GetRequiredService<NumberSeriesService>();
			PharmacyProfile pharmacyProfile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
			_pharmacyName = pharmacyProfile.Name;
			_upiId = pharmacyProfile.UpiId ?? string.Empty;
			InvoiceNoPreview = await series.PreviewNextAsync(BranchService.WholesaleInvoicePrefix, null, cancellationToken);
			await RefreshBuyerHeaderAsync(cancellationToken);
		}
		finally
		{
			IsStockChoicesLoading = false;
			OnPropertyChanged("StockPickerPlaceholder");
			OnPropertyChanged(nameof(HasMedicinePickerItems));
		}
	}

	private void SeedMedicinePickerFromStock()
	{
		MedicinePickerItems.Clear();
		foreach (WholesaleStockChoice choice in StockChoices.Take(40))
		{
			MedicinePickerItems.Add(WholesaleMedicinePickerItem.FromInStock(choice));
		}
		OnPropertyChanged(nameof(HasMedicinePickerItems));
		OnPropertyChanged(nameof(StockPickerPlaceholder));
	}

	private async Task DebouncedMedicineSearchAsync()
	{
		_medicineSearchCts?.Cancel();
		CancellationTokenSource cts = new CancellationTokenSource();
		_medicineSearchCts = cts;
		try
		{
			await Task.Delay(220, cts.Token);
			await SearchMedicinesAsync(MedicineSearchText, cts.Token);
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task SearchMedicinesAsync(string query, CancellationToken cancellationToken)
	{
		_explicitMedicinePick = false;
		string needle = query?.Trim() ?? string.Empty;
		if (needle.Length < 2)
		{
			SeedMedicinePickerFromStock();
			IsMedicineDropDownOpen = MedicinePickerItems.Count > 0 && !string.IsNullOrWhiteSpace(needle);
			OnPropertyChanged(nameof(ShowOnlineLookup));
			return;
		}

		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);

		List<Drug> drugs = await context.Drugs.AsNoTracking()
			.Where(d => d.IsActive && (
				d.Name.Contains(needle)
				|| (d.GenericName != null && d.GenericName.Contains(needle))
				|| (d.BrandName != null && d.BrandName.Contains(needle))))
			.OrderBy(d => d.Name)
			.Take(40)
			.ToListAsync(cancellationToken);
		Guid[] drugIds = drugs.Select(d => d.Id).ToArray();
		List<Batch> batches = drugIds.Length == 0
			? new List<Batch>()
			: await context.Batches.AsNoTracking().Where(b => drugIds.Contains(b.DrugId)).ToListAsync(cancellationToken);
		Guid[] batchIds = batches.Select(b => b.Id).ToArray();
		Dictionary<Guid, decimal> stock = batchIds.Length == 0
			? new Dictionary<Guid, decimal>()
			: await (from movement in context.StockMovements.AsNoTracking()
				where batchIds.Contains(movement.BatchId)
				group movement by movement.BatchId into g
				select new { BatchId = g.Key, Quantity = g.Sum(m => m.QuantityChange) })
				.ToDictionaryAsync(x => x.BatchId, x => x.Quantity, cancellationToken);
		List<DrugPackLevel> packLevels = drugIds.Length == 0
			? new List<DrugPackLevel>()
			: await context.DrugPackLevels.AsNoTracking().Where(p => drugIds.Contains(p.DrugId)).ToListAsync(cancellationToken);
		Dictionary<Guid, List<DrugPackLevel>> packsByDrug = packLevels.GroupBy(p => p.DrugId).ToDictionary(g => g.Key, g => g.ToList());

		List<WholesaleMedicinePickerItem> inStock = new();
		List<WholesaleMedicinePickerItem> outOfStock = new();
		HashSet<Guid> drugsWithBatchRows = new();
		foreach (Batch batch in batches.OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue))
		{
			Drug? drug = drugs.FirstOrDefault(d => d.Id == batch.DrugId);
			if (drug == null)
			{
				continue;
			}

			if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < today)
			{
				continue;
			}

			drugsWithBatchRows.Add(drug.Id);
			decimal available = stock.GetValueOrDefault(batch.Id);
			WholesaleStockChoice choice = BuildStockChoice(drug, batch, available, packsByDrug.GetValueOrDefault(drug.Id));
			if (available > 0m)
			{
				inStock.Add(WholesaleMedicinePickerItem.FromInStock(choice));
			}
			else
			{
				outOfStock.Add(WholesaleMedicinePickerItem.FromOutOfStock(choice));
			}
		}

		// Local drugs with no batches at all → treat as needing inward.
		foreach (Drug drug in drugs.Where(d => !drugsWithBatchRows.Contains(d.Id)).Take(15))
		{
			outOfStock.Add(new WholesaleMedicinePickerItem
			{
				GroupKey = "Out of stock",
				DisplayTitle = drug.Name,
				DisplayDetail = "Out of Stock - Click to Add Stock / Batch",
				Display = $"[Out of stock] {drug.Name} — Out of Stock - Click to Add Stock / Batch",
				DrugId = drug.Id,
				MedicineName = drug.Name,
				Composition = drug.GenericName,
				SuggestedMrp = drug.Mrp
			});
		}

		List<WholesaleMedicinePickerItem> catalogue = new();
		try
		{
			MedicineSearchResults catalogResults = await scope.ServiceProvider.GetRequiredService<CatalogSearchService>()
				.SearchAsync(needle, cancellationToken);
			HashSet<Guid> shownCatalog = drugs.Where(d => d.CatalogMedicineId.HasValue).Select(d => d.CatalogMedicineId!.Value).ToHashSet();
			HashSet<Guid> shownDrugs = drugs.Select(d => d.Id).ToHashSet();
			foreach (MedicineSearchResult row in catalogResults.FromCatalog.Take(20))
			{
				if (row.DrugId is Guid existingDrug && shownDrugs.Contains(existingDrug))
				{
					continue;
				}

				if (row.CatalogMedicineId == Guid.Empty)
				{
					catalogue.Add(new WholesaleMedicinePickerItem
					{
						GroupKey = "Out of stock",
						DisplayTitle = row.Name,
						DisplayDetail = "Out of Stock - Click to Add Stock / Batch",
						Display = "[Out of stock] " + row.Name + " — Out of Stock - Click to Add Stock / Batch",
						DrugId = row.DrugId,
						MedicineName = row.Name,
						Composition = row.Composition,
						Manufacturer = row.Manufacturer,
						SuggestedMrp = row.Mrp ?? row.ReferencePrice
					});
					continue;
				}

				if (shownCatalog.Contains(row.CatalogMedicineId))
				{
					continue;
				}

				catalogue.Add(WholesaleMedicinePickerItem.FromCatalogue(
					row.CatalogMedicineId,
					row.Name,
					row.Composition,
					row.Manufacturer,
					row.Mrp ?? row.ReferencePrice,
					row.DrugId));
			}
		}
		catch
		{
			// Catalogue search is best-effort; local stock results still show.
		}

		MedicinePickerItems.Clear();
		foreach (WholesaleMedicinePickerItem item in inStock.Take(25))
		{
			MedicinePickerItems.Add(item);
		}
		foreach (WholesaleMedicinePickerItem item in outOfStock.Take(15))
		{
			MedicinePickerItems.Add(item);
		}
		foreach (WholesaleMedicinePickerItem item in catalogue)
		{
			MedicinePickerItems.Add(item);
		}

		if (inStock.Count == 0 && outOfStock.Count == 0 && catalogue.Count == 0)
		{
			MedicinePickerItems.Insert(0, WholesaleMedicinePickerItem.QuickAdd(needle));
		}

		OnPropertyChanged(nameof(HasMedicinePickerItems));
		OnPropertyChanged(nameof(StockPickerPlaceholder));
		OnPropertyChanged(nameof(ShowNamedQuickAdd));
		OnPropertyChanged(nameof(AddNamedMedicineCaption));
		OnPropertyChanged(nameof(ShowCreateUnlisted));
		OnPropertyChanged(nameof(CreateUnlistedCaption));
		OnPropertyChanged(nameof(ShowOnlineLookup));
		IsMedicineDropDownOpen = MedicinePickerItems.Count > 0;
	}

	private async Task ExtractOrderDocumentsAsync()
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		IReadOnlyList<string> files = scope.ServiceProvider.GetRequiredService<IFilePickerService>().PickInvoiceDocuments();
		if (files.Count == 0)
		{
			return;
		}

		if (StockChoices.Count == 0)
		{
			await LoadAsync();
		}

		InvoiceDocumentExtractor extractor = scope.ServiceProvider.GetRequiredService<InvoiceDocumentExtractor>();
		int added = 0;
		List<string> missed = new List<string>();
		foreach (string path in files)
		{
			try
			{
				ExtractedPurchaseInvoice invoice = await extractor.ExtractAsync(path);
				foreach (ExtractedPurchaseLine item in invoice.Items)
				{
					WholesaleStockChoice? choice = StockChoices
						.Where(stock => stock.DrugName.Contains(item.ItemName.Trim(), StringComparison.OrdinalIgnoreCase) && stock.Available > 0m)
						.OrderBy(stock => stock.ExpiryDate ?? DateOnly.MaxValue)
						.FirstOrDefault();
					if (choice == null)
					{
						missed.Add(item.ItemName);
						continue;
					}

					SelectedStockToAdd = choice;
					await AddLineAsync();
					if (SelectedLine != null && item.Quantity > 0m)
					{
						SelectedLine.Quantity = item.Quantity;
					}

					added++;
				}
			}
			catch (Exception ex)
			{
				missed.Add(System.IO.Path.GetFileName(path) + " (" + ex.Message + ")");
			}
		}

		StatusMessage = added + " order line(s) added to the wholesale bill.";
		ErrorMessage = missed.Count == 0 ? string.Empty : "Not in stock: " + string.Join(", ", missed.Distinct(StringComparer.OrdinalIgnoreCase));
	}

	private async Task SearchOnlineAndInwardAsync()
	{
		string typed = MedicineSearchText.Trim();
		SmartDrugLookupOutcome outcome = await _smartLookup.LookupAsync(typed);
		await QuickAddMedicineAsync(outcome.Suggestion?.BrandName ?? typed, outcome.Suggestion, outcome.Message);
	}

	private async Task QuickAddMedicineAsync(string typedName, SmartDrugSuggestion? suggestion = null, string? notice = null)
	{
		CustomMedicineResult? saved = await _quickAdd.ShowAsync(typedName, suggestion, notice);
		if (saved == null)
		{
			return;
		}

		await OpenQuickInwardAsync(new WholesaleMedicinePickerItem
		{
			GroupKey = "New medicine",
			DisplayTitle = saved.Name,
			DisplayDetail = "New medicine master item",
			Display = saved.Name,
			DrugId = saved.DrugId,
			CatalogMedicineId = saved.CatalogMedicineId,
			MedicineName = saved.Name,
			SuggestedMrp = saved.Mrp,
			Composition = saved.GenericName
		}, saved.PurchaseRate, "COUNTER", 1m, 1m);
	}

	private async Task OnMedicineItemSelectedAsync(WholesaleMedicinePickerItem item)
	{
		ErrorMessage = string.Empty;
		if (item.IsQuickAdd)
		{
			IsMedicineDropDownOpen = false;
			await QuickAddMedicineAsync(item.MedicineName);
			return;
		}

		if (item.IsInStock && item.StockChoice != null)
		{
			SelectedStockToAdd = item.StockChoice;
			_suppressMedicineSearchSync = true;
			try
			{
				MedicineSearchText = item.DisplayTitle;
			}
			finally
			{
				_suppressMedicineSearchSync = false;
			}
			IsMedicineDropDownOpen = false;
			await AddLineAsync();
			return;
		}

		IsMedicineDropDownOpen = false;
		await OpenQuickInwardAsync(item);
	}

	private async Task OpenQuickInwardAsync(WholesaleMedicinePickerItem item, decimal? purchaseRate = null, string? defaultBatchNo = null, decimal? defaultQuantity = null, decimal? invoiceQuantity = null)
	{
		QuickInwardWindow dialog = new QuickInwardWindow(
			_scopeFactory,
			_session,
			item.MedicineName,
			item.DrugId,
			item.CatalogMedicineId,
			item.Composition,
			item.Manufacturer,
			item.SuggestedMrp ?? item.StockChoice?.Mrp,
			purchaseRate,
			defaultBatchNo,
			defaultQuantity);
		try
		{
			Window? owner = Application.Current?.MainWindow;
			if (owner != null)
			{
				dialog.Owner = owner;
			}
		}
		catch
		{
		}

		if (dialog.ShowDialog() != true || dialog.Result == null)
		{
			_suppressMedicineSearchSync = true;
			try
			{
				SelectedMedicineItem = null;
			}
			finally
			{
				_suppressMedicineSearchSync = false;
			}
			return;
		}

		AddStockResult result = dialog.Result;
		await ReloadStockChoicesAsync();
		WholesaleStockChoice? created = StockChoices.FirstOrDefault(c => c.BatchId == result.BatchId)
			?? await BuildStockChoiceByBatchIdAsync(result.BatchId);
		if (created == null)
		{
			ErrorMessage = "Batch was saved but could not be loaded into the billing picker.";
			return;
		}

		SelectedStockToAdd = created;
		_suppressMedicineSearchSync = true;
		try
		{
			MedicineSearchText = created.DrugName;
			SelectedMedicineItem = WholesaleMedicinePickerItem.FromInStock(created);
		}
		finally
		{
			_suppressMedicineSearchSync = false;
		}

		decimal qty = invoiceQuantity ?? (dialog.QuantityEntered > 0m ? dialog.QuantityEntered : 1m);
		decimal unitPrice = dialog.PtrEntered > 0m ? dialog.PtrEntered : created.DefaultWholesaleRate;
		WholesaleInvoiceLineDraft draft = new WholesaleInvoiceLineDraft
		{
			StockChoice = created,
			Quantity = qty,
			FreeQuantity = 0m,
			UnitPrice = unitPrice,
			SaleUnit = "Unit"
		};
		draft.SetBatchOptions(StockChoices.Where(choice => choice.DrugId == created.DrugId));
		draft.MrpCommitted += OnWholesaleMrpCommitted;
		Items.Add(draft);
		SelectedLine = draft;
		NotifyTotalsChanged();
		ClearMedicinePickerAfterAdd();
		StatusMessage = $"Batch {created.BatchNo} inwarded for {created.DrugName} and added to the invoice.";
		if (draft.Mrp <= 0m)
		{
			PromptMissingWholesaleMrp(draft);
		}
		else
		{
			RequestFocusQty();
		}
		ErrorMessage = string.Empty;
	}

	private async Task ReloadStockChoicesAsync()
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		List<Drug> drugs = await context.Drugs.AsNoTracking().Where(d => d.IsActive).ToListAsync();
		Guid[] drugIds = drugs.Select(d => d.Id).ToArray();
		List<Batch> batches = await context.Batches.AsNoTracking().Where(b => drugIds.Contains(b.DrugId)).ToListAsync();
		Guid[] batchIds = batches.Select(b => b.Id).ToArray();
		List<DrugPackLevel> packLevels = await context.DrugPackLevels.AsNoTracking().Where(p => drugIds.Contains(p.DrugId)).ToListAsync();
		Dictionary<Guid, List<DrugPackLevel>> packsByDrug = packLevels.GroupBy(p => p.DrugId).ToDictionary(g => g.Key, g => g.ToList());
		Dictionary<Guid, decimal> stock = await (from movement in context.StockMovements.AsNoTracking()
			where batchIds.Contains(movement.BatchId)
			group movement by movement.BatchId into g
			select new { BatchId = g.Key, Quantity = g.Sum(m => m.QuantityChange) })
			.ToDictionaryAsync(x => x.BatchId, x => x.Quantity);
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<WholesaleStockChoice> list = (from batch in batches
			join drug in drugs on batch.DrugId equals drug.Id
			let available = stock.GetValueOrDefault(batch.Id)
			where available > 0m && (!batch.ExpiryDate.HasValue || batch.ExpiryDate.Value >= today)
			orderby batch.ExpiryDate ?? DateOnly.MaxValue, drug.Name
			select BuildStockChoice(drug, batch, available, packsByDrug.GetValueOrDefault(drug.Id))).ToList();
		StockChoices.Clear();
		foreach (WholesaleStockChoice choice in list)
		{
			StockChoices.Add(choice);
		}
		OnPropertyChanged(nameof(HasStockChoices));
		SeedMedicinePickerFromStock();
	}

	private async Task<WholesaleStockChoice?> BuildStockChoiceByBatchIdAsync(Guid batchId)
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		Batch? batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId);
		if (batch == null)
		{
			return null;
		}

		Drug? drug = await context.Drugs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == batch.DrugId);
		if (drug == null)
		{
			return null;
		}

		decimal available = await context.StockMovements.AsNoTracking()
			.Where(m => m.BatchId == batchId)
			.SumAsync(m => (decimal?)m.QuantityChange) ?? 0m;
		List<DrugPackLevel> packs = await context.DrugPackLevels.AsNoTracking().Where(p => p.DrugId == drug.Id).ToListAsync();
		return BuildStockChoice(drug, batch, available, packs);
	}

	private async Task ShowPaymentQrAsync()
	{
		ErrorMessage = string.Empty;
		if (EstimatedTotal <= 0m)
		{
			ErrorMessage = "Add invoice lines before showing the payment QR.";
			return;
		}
		if (string.IsNullOrWhiteSpace(_upiId))
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmacyProfile pharmacyProfile = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().PharmacyProfiles.AsNoTracking().SingleAsync();
			_pharmacyName = pharmacyProfile.Name;
			_upiId = pharmacyProfile.UpiId ?? string.Empty;
		}
		decimal num = EstimatedTotal;
		if (PaidAmount > 0m)
		{
			num = PaidAmount;
		}
		ImageSource qrImage = null;
		string text = _documentOutputSettings.Load().UpiPayeeName;
		if (string.IsNullOrWhiteSpace(text))
		{
			text = _pharmacyName;
		}
		try
		{
			if (!string.IsNullOrWhiteSpace(_upiId))
			{
				qrImage = QrCodeImage.FromText(UpiPaymentPayload.Build(_upiId, text, num, InvoiceNoPreview), 600);
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
			return;
		}
		PaymentQrDialogResult paymentQrDialogResult = _paymentQrDialogs.Show(new PaymentQrDialogRequest(_upiId, text, num, InvoiceNoPreview, qrImage, _documentOutputSettings.Load().ShowCustomerQrWindow));
		if (paymentQrDialogResult.Outcome == PaymentQrDialogOutcome.ConfigureUpi)
		{
			_settingsPage.RevealUpiConfiguration();
			_navigation.Navigate("Settings");
			StatusMessage = "Configure UPI & Digital Payment Settings, then return to Wholesale Sales.";
		}
		else if (paymentQrDialogResult.Outcome == PaymentQrDialogOutcome.Confirmed)
		{
			_suppressUpiAutoModal = true;
			try
			{
				PaymentMethod = "UPI / QR";
				PaidAmount = num;
			}
			finally
			{
				_suppressUpiAutoModal = false;
			}
			await SaveInvoiceAsync();
		}
	}

	public async Task<bool> TryAddByBarcodeAsync(string raw)
	{
		Gs1Scan scan = Gs1Scan.Parse(raw);
		IReadOnlyList<string> keys = scan.LookupKeys();
		if (keys.Count == 0 && string.IsNullOrWhiteSpace(scan.Batch))
		{
			return false;
		}

		try
		{
			WholesaleStockChoice? match = null;
			if (!string.IsNullOrWhiteSpace(scan.Batch))
			{
				match = StockChoices.FirstOrDefault(choice => string.Equals(choice.BatchNo, scan.Batch, StringComparison.OrdinalIgnoreCase));
			}

			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			Drug? drug = keys.Count == 0
				? null
				: await context.Drugs.AsNoTracking().FirstOrDefaultAsync(item => item.IsActive && item.Barcode != null && keys.Contains(item.Barcode));
			if (drug != null)
			{
				List<WholesaleStockChoice> forDrug = StockChoices.Where(choice => choice.DrugId == drug.Id)
					.OrderBy(choice => choice.ExpiryDate ?? DateOnly.MaxValue)
					.ToList();
				if (!string.IsNullOrWhiteSpace(scan.Batch))
				{
					match = forDrug.FirstOrDefault(choice => string.Equals(choice.BatchNo, scan.Batch, StringComparison.OrdinalIgnoreCase)) ?? forDrug.FirstOrDefault() ?? match;
				}
				else
				{
					match = forDrug.FirstOrDefault() ?? match;
				}
			}
			else if (match != null && string.IsNullOrWhiteSpace(scan.Gtin) && string.IsNullOrWhiteSpace(scan.Batch))
			{
				match = StockChoices.Where(choice => choice.DrugId == match.DrugId)
					.OrderBy(choice => choice.ExpiryDate ?? DateOnly.MaxValue)
					.FirstOrDefault();
			}

			if (match == null)
			{
				return false;
			}

			_suppressMedicineSearchSync = true;
			try
			{
				SelectedMedicineItem = null;
			}
			finally
			{
				_suppressMedicineSearchSync = false;
			}
			SelectedStockToAdd = match;
			await AddLineAsync();
			StatusMessage = string.IsNullOrWhiteSpace(scan.Batch)
				? "Scanned " + match.DrugName + ". Oldest valid batch " + match.BatchNo + " was selected. Change the batch dropdown if needed."
				: "Scanned 2D code: " + match.DrugName + " · batch " + match.BatchNo + ".";
			return true;
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
			return false;
		}
	}

	private async Task AddLineAsync()
	{
		if (SelectedMedicineItem is { NeedsInward: true })
		{
			await OpenQuickInwardAsync(SelectedMedicineItem);
			return;
		}

		WholesaleStockChoice wholesaleStockChoice = SelectedStockToAdd ?? StockChoices.FirstOrDefault();
		if ((object)wholesaleStockChoice == null)
		{
			ErrorMessage = "Search and select an in-stock batch, or add a batch from the Drug Bank catalogue first.";
			return;
		}

		if (wholesaleStockChoice.Available <= 0m)
		{
			ErrorMessage = "Out of stock — add a batch via Quick Inward before billing this medicine.";
			await OpenQuickInwardAsync(WholesaleMedicinePickerItem.FromOutOfStock(wholesaleStockChoice));
			return;
		}

		decimal unitPrice = wholesaleStockChoice.DefaultWholesaleRate;
		decimal freeQuantity = 0m;
		if (SelectedCustomer != null)
		{
			try
			{
				using IServiceScope scope = _scopeFactory.CreateScope();
				PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
				Drug? drug = await context.Drugs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == wholesaleStockChoice.DrugId);
				Batch? batch = await context.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == wholesaleStockChoice.BatchId);
				if (drug != null && batch != null)
				{
					WholesalePriceQuote quote = await scope.ServiceProvider.GetRequiredService<WholesalePricingService>()
						.GetQuoteAsync(SelectedCustomer, drug, batch, 1m, DateOnly.FromDateTime(DateTime.Today));
					unitPrice = quote.UnitRate;
					freeQuantity = quote.FreeQuantity;
				}
			}
			catch
			{
				// Fall back to batch PTR/MRP when category pricing is unavailable.
			}
		}

		WholesaleInvoiceLineDraft draft = new WholesaleInvoiceLineDraft
		{
			StockChoice = wholesaleStockChoice,
			Quantity = 1m,
			FreeQuantity = freeQuantity,
			UnitPrice = unitPrice,
			SaleUnit = "Unit"
		};
		draft.SetBatchOptions(StockChoices.Where(choice => choice.DrugId == wholesaleStockChoice.DrugId));
		draft.MrpCommitted += OnWholesaleMrpCommitted;
		Items.Add(draft);
		SelectedLine = draft;
		NotifyTotalsChanged();
		ClearMedicinePickerAfterAdd();
		if (draft.Mrp <= 0m)
		{
			PromptMissingWholesaleMrp(draft);
		}
		else
		{
			RequestFocusQty();
		}
	}

	private void OnWholesaleMrpCommitted(WholesaleInvoiceLineDraft line, decimal mrp)
	{
		if (line.StockChoice == null)
		{
			return;
		}

		_ = SaveWholesaleMrpAsync(line.StockChoice.BatchId, line.StockChoice.DrugName, line.StockChoice.BatchNo, mrp);
	}

	private void PromptMissingWholesaleMrp(WholesaleInvoiceLineDraft line)
	{
		if (line.StockChoice == null)
		{
			return;
		}

		string message = "Enter MRP for " + line.StockChoice.DrugName + " (Batch: " + line.StockChoice.BatchNo + ")";
		string? entered;
		using (IServiceScope scope = _scopeFactory.CreateScope())
		{
			entered = scope.ServiceProvider.GetRequiredService<IPromptService>().AskText("MRP required", message, "MRP (₹)");
		}

		if (MrpAmount.TryParse(entered, out decimal mrp) && mrp > 0m)
		{
			line.ApplySavedMrp(mrp);
			_ = SaveWholesaleMrpAsync(line.StockChoice.BatchId, line.StockChoice.DrugName, line.StockChoice.BatchNo, mrp);
			RequestFocusQty();
			return;
		}

		StatusMessage = message;
		RequestLineFocus("Mrp");
	}

	private async Task SaveWholesaleMrpAsync(Guid batchId, string medicineName, string batchNo, decimal mrp)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<InventoryService>().UpdateBatchMrpAsync(batchId, mrp);
			StatusMessage = "MRP saved for " + medicineName + " · batch " + batchNo + ".";
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private void ClearMedicinePickerAfterAdd()
	{
		_suppressMedicineSearchSync = true;
		try
		{
			SelectedMedicineItem = null;
			MedicineSearchText = string.Empty;
			SelectedStockToAdd = null;
			IsMedicineDropDownOpen = false;
		}
		finally
		{
			_suppressMedicineSearchSync = false;
		}
	}

	public string LineFocusField
	{
		get => _lineFocusField;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_lineFocusField, value))
			{
				_lineFocusField = value;
				OnPropertyChanged(nameof(LineFocusField));
			}
		}
	}

	private void RequestFocusQty()
	{
		RequestLineFocus("Qty");
	}

	private void RequestLineFocus(string field)
	{
		LineFocusField = field;
		FocusQtyRequest = string.Empty;
		FocusQtyRequest = field + "-" + Guid.NewGuid().ToString("N");
	}

	private void RequestFocus(string target)
	{
		FocusRequest = string.Empty;
		FocusRequest = target;
	}

	private void FocusCustomer() => RequestFocus("CustomerSearch");

	private void FocusMedicine() => RequestFocus("MedicineSearch");

	private void FocusPayment() => RequestFocus("Payment");

	private void NewBill()
	{
		if (Items.Count > 0 && !_confirmation.Confirm("Clear the current unsaved wholesale invoice?", "New invoice"))
		{
			return;
		}

		Items.Clear();
		SelectedCustomer = null;
		CustomerSearchText = string.Empty;
		ClearMedicinePickerAfterAdd();
		PaidAmount = 0m;
		PaymentReference = string.Empty;
		PaymentMethod = "Cash";
		ConfirmNearExpiry = false;
		OverrideCreditOrOverdue = false;
		OverrideReason = string.Empty;
		TransportDetails = string.Empty;
		VehicleNumber = string.Empty;
		EWayBillNumber = string.Empty;
		Irn = string.Empty;
		Notes = string.Empty;
		ErrorMessage = string.Empty;
		StatusMessage = "New wholesale invoice — select a retailer to begin.";
		NotifyTotalsChanged();
		RequestFocus("CustomerSearch");
	}

	private void RemoveSelectedLine()
	{
		if (SelectedLine == null)
		{
			return;
		}

		if (!_confirmation.Confirm($"Remove {SelectedLine.StockChoice?.DrugName ?? "this line"} from the invoice?", "Remove line"))
		{
			return;
		}

		RemoveLine(SelectedLine);
		SelectedLine = null;
		StatusMessage = "Line removed. Press Ctrl+Z is not available — re-add from medicine search if needed.";
	}

	private static WholesaleStockChoice BuildStockChoice(Drug drug, Batch batch, decimal available, List<DrugPackLevel>? packs)
	{
		decimal unitsPerStrip = packs?.FirstOrDefault(p => p.Level == PackLevel.Strip)?.UnitsPerPack
			?? packs?.FirstOrDefault(p => p.Level == PackLevel.Unit)?.UnitsPerPack
			?? 0m;
		decimal unitsPerBox = packs?.FirstOrDefault(p => p.Level is PackLevel.Box or PackLevel.Carton)?.UnitsPerPack ?? 0m;
		string? packLabel = packs?.OrderByDescending(p => p.UnitsPerPack).Select(p => p.Label).FirstOrDefault();
		if (string.IsNullOrWhiteSpace(packLabel))
		{
			packLabel = unitsPerBox > 0m ? $"Box={unitsPerBox}" : (unitsPerStrip > 0m ? $"Strip={unitsPerStrip}" : "Unit");
		}

		return new WholesaleStockChoice(
			drug.Id,
			batch.Id,
			drug.Name,
			batch.BatchNo,
			batch.ExpiryDate,
			available,
			batch.Mrp ?? drug.Mrp.GetValueOrDefault(),
			batch.Ptr,
			packLabel,
			unitsPerStrip,
			unitsPerBox,
			drug.Schedule,
			drug.GstRate.GetValueOrDefault());
	}

	private void RemoveLine(WholesaleInvoiceLineDraft? line)
	{
		if (line != null)
		{
			Items.Remove(line);
			NotifyTotalsChanged();
		}
	}

	private async Task SaveInvoiceAsync()
	{
		ErrorMessage = string.Empty;
		AppUser appUser = _session.User ?? throw new UnauthorizedAccessException("Sign in before creating invoices.");
		if (SelectedCustomer == null || Items.Count == 0 || Items.Any((WholesaleInvoiceLineDraft item) => (object)item.StockChoice == null))
		{
			ErrorMessage = "Choose an active retailer / pharmacy and at least one stocked item.";
			_confirmation.Notify("Cannot Save Invoice Yet", "1. Select the retailer / pharmacy.\n2. Search a medicine (F4) and add at least one stocked line.");
			return;
		}
		if (string.IsNullOrWhiteSpace(BuyerDrugLicence))
		{
			ErrorMessage = "Buyer Drug License (DL) Number is required for wholesale B2B invoices (Form 20B / 21B).";
			return;
		}
		WholesaleInvoiceLineDraft[] array = Items.Where((WholesaleInvoiceLineDraft item) =>
		{
			DateOnly? dateOnly = item.StockChoice?.ExpiryDate;
			return dateOnly.HasValue && dateOnly.GetValueOrDefault().DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber <= 90;
		}).ToArray();
		if (array.Length != 0 && !ConfirmNearExpiry)
		{
			if (!_confirmation.Confirm("The invoice includes near-expiry batches: " + string.Join(", ", array.Select((WholesaleInvoiceLineDraft line) => line.StockChoice.BatchNo)) + ". Confirm?", "Near-expiry stock"))
			{
				return;
			}
			ConfirmNearExpiry = true;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			WholesaleInvoiceResult wholesaleInvoiceResult = await scope.ServiceProvider.GetRequiredService<WholesaleInvoiceService>().SaveAsync(new SaveWholesaleInvoiceInput(SelectedCustomer.Id, Items.Select((WholesaleInvoiceLineDraft item) => new WholesaleInvoiceLineInput(item.StockChoice.DrugId, item.StockChoice.BatchId, item.InventoryQuantity, item.InventoryFreeQuantity, item.UnitPrice, item.DiscountAmount)).ToArray(), PaidAmount, UpiPaymentPayload.NormalizeMethod(PaymentMethod), PaymentReference, ConfirmNearExpiry, 90, OverrideCreditOrOverdue, OverrideReason, TransportDetails, VehicleNumber, EWayBillNumber, Irn, Notes), appUser.Id, appUser.Role);
			LastInvoiceId = wholesaleInvoiceResult.Invoice.Id;
			StatusMessage = $"Invoice {wholesaleInvoiceResult.Invoice.InvoiceNo} saved. Total {MoneyFormat.Rupees(wholesaleInvoiceResult.Invoice.TotalAmount)}; outstanding {MoneyFormat.Rupees(wholesaleInvoiceResult.OutstandingAfterPosting)}.";
			ErrorMessage = string.Empty;
			Items.Clear();
			await LoadAsync();
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is ArgumentException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task SaveAndPrintAsync()
	{
		await SaveInvoiceAsync();
		if (LastInvoiceId.HasValue && string.IsNullOrWhiteSpace(ErrorMessage))
		{
			try
			{
				string path = await _documents.ExportAsync(LastInvoiceId.Value, deliveryChallan: false);
				StatusMessage = "Tax invoice saved and exported: " + path;
				try
				{
					Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
				}
				catch
				{
				}
			}
			catch (Exception ex)
			{
				ErrorMessage = "Invoice saved but tax invoice PDF failed: " + ex.Message;
			}
		}
	}

	private async Task ExportTaxInvoiceAsync()
	{
		if (LastInvoiceId.HasValue)
		{
			StatusMessage = await _documents.ExportAsync(LastInvoiceId.Value, deliveryChallan: false);
		}
	}

	private async Task ExportDeliveryChallanAsync()
	{
		if (LastInvoiceId.HasValue)
		{
			StatusMessage = await _documents.ExportAsync(LastInvoiceId.Value, deliveryChallan: true);
		}
	}

	private void NotifyTotalsChanged()
	{
		OnPropertyChanged("Subtotal");
		OnPropertyChanged("EstimatedTax");
		OnPropertyChanged("EstimatedTotal");
		RefreshCreditGateAsync();
	}

	private async Task RefreshCreditGateAsync()
	{
		if (SelectedCustomer == null)
		{
			CreditLockAlert = string.Empty;
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			CreditGateAlert creditGateAlert = await scope.ServiceProvider.GetRequiredService<IDunningService>().EvaluateCreditGateAsync(SelectedCustomer.Id, EstimatedTotal);
			WholesaleBillingPageViewModel wholesaleBillingPageViewModel = this;
			string creditLockAlert;
			if (creditGateAlert.IsBlocked)
			{
				creditLockAlert = (string.IsNullOrWhiteSpace(creditGateAlert.Message) ? "Credit Limit Exceeded - Admin Override Required" : creditGateAlert.Message);
			}
			else
			{
				creditLockAlert = string.Empty;
			}
			wholesaleBillingPageViewModel.CreditLockAlert = creditLockAlert;
		}
		catch
		{
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedCustomerChanged(Customer? value)
	{
		OnPropertyChanged(nameof(HasSelectedCustomer));
		OnPropertyChanged(nameof(ShowRetailerSearchPlaceholder));
		OnPropertyChanged(nameof(ShowAddNewRetailerOption));
		OnPropertyChanged(nameof(AddNewRetailerCaption));
		if (value != null)
		{
			_suppressCustomerSearchSync = true;
			try
			{
				CustomerSearchText = value.Name ?? string.Empty;
			}
			finally
			{
				_suppressCustomerSearchSync = false;
			}

			IsCustomerDropDownOpen = false;
		}

		RefreshCreditGateAsync();
		_ = RefreshBuyerHeaderAsync();
	}

	private void ApplyCustomerFilter(bool openDropDown = false)
	{
		string needle = CustomerSearchText?.Trim() ?? string.Empty;
		IEnumerable<Customer> source = Customers;
		if (!string.IsNullOrWhiteSpace(needle))
		{
			source = Customers.Where(c =>
				(!string.IsNullOrWhiteSpace(c.Name) && c.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
				|| (!string.IsNullOrWhiteSpace(c.Gstin) && c.Gstin.Contains(needle, StringComparison.OrdinalIgnoreCase))
				|| (!string.IsNullOrWhiteSpace(c.Phone) && c.Phone.Contains(needle, StringComparison.OrdinalIgnoreCase)));
		}

		FilteredCustomers.Clear();
		foreach (Customer customer in source.Take(200))
		{
			FilteredCustomers.Add(customer);
		}

		OnPropertyChanged(nameof(ShowAddNewRetailerOption));
		OnPropertyChanged(nameof(AddNewRetailerCaption));

		if (openDropDown && !string.IsNullOrWhiteSpace(needle) && (FilteredCustomers.Count > 0 || ShowAddNewRetailerOption))
		{
			IsCustomerDropDownOpen = FilteredCustomers.Count > 0;
		}
	}

	private void TrySelectExactCustomerMatch()
	{
		string needle = CustomerSearchText?.Trim() ?? string.Empty;
		if (needle.Length == 0)
		{
			if (SelectedCustomer != null)
			{
				SelectedCustomer = null;
			}

			return;
		}

		Customer? exact = Customers.FirstOrDefault(c => string.Equals(c.Name?.Trim(), needle, StringComparison.OrdinalIgnoreCase));
		if (exact != null)
		{
			if (!ReferenceEquals(SelectedCustomer, exact) && SelectedCustomer?.Id != exact.Id)
			{
				SelectedCustomer = exact;
			}
		}
		else if (SelectedCustomer != null
		         && !string.Equals(SelectedCustomer.Name?.Trim(), needle, StringComparison.OrdinalIgnoreCase))
		{
			// User is editing away from the selected retailer — clear selection until they pick again.
			SelectedCustomer = null;
		}
	}

	private async Task AddNewRetailerAsync()
	{
		ErrorMessage = string.Empty;
		string suggestedName = CustomerSearchText?.Trim() ?? string.Empty;
		QuickAddRetailerWindow dialog = new QuickAddRetailerWindow(suggestedName);
		try
		{
			Window? owner = Application.Current?.MainWindow;
			if (owner != null)
			{
				dialog.Owner = owner;
			}
		}
		catch
		{
		}

		if (dialog.ShowDialog() != true)
		{
			return;
		}

		AppUser appUser = _session.User ?? throw new UnauthorizedAccessException("Sign in before adding retailers.");
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			WholesaleCustomerService service = scope.ServiceProvider.GetRequiredService<WholesaleCustomerService>();
			Customer customer = new Customer
			{
				Name = dialog.RetailerName,
				BuyerType = "Retailer",
				Gstin = dialog.Gstin,
				Phone = dialog.Phone,
				Address = dialog.Address,
				CreditDays = dialog.CreditDays,
				IsActive = true
			};
			CustomerLicence licence = new CustomerLicence
			{
				LicenceType = dialog.LicenceType,
				LicenceNumber = dialog.LicenceNumber,
				IssuedOn = dialog.IssuedOn,
				ExpiresOn = dialog.ExpiresOn
			};
			await service.SaveAsync(customer, new[] { licence }, new[] { dialog.LicenceType }, appUser.Id);

			Customers.Insert(0, customer);
			ApplyCustomerFilter();
			SelectedCustomer = customer;
			StatusMessage = $"Retailer '{customer.Name}' saved to Customers master with DL {dialog.LicenceType} {dialog.LicenceNumber}.";
			ErrorMessage = string.Empty;
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UnauthorizedAccessException)
		{
			ErrorMessage = ex.Message;
		}
		catch (Exception ex)
		{
			ErrorMessage = "Could not save retailer: " + ex.Message;
		}
	}

	private async Task RefreshBuyerHeaderAsync(CancellationToken cancellationToken = default)
	{
		Customer? customer = SelectedCustomer;
		if (customer == null)
		{
			BuyerDrugLicence = string.Empty;
			BuyerGstin = string.Empty;
			BuyerPhone = string.Empty;
			BuyerAddress = string.Empty;
			BuyerCreditDays = 0;
			BuyerDueDateDisplay = string.Empty;
			return;
		}

		BuyerGstin = string.IsNullOrWhiteSpace(customer.Gstin) ? "Unregistered / composite (optional)" : customer.Gstin.Trim();
		BuyerPhone = customer.Phone?.Trim() ?? string.Empty;
		BuyerAddress = customer.Address?.Trim() ?? string.Empty;
		BuyerCreditDays = customer.CreditDays;
		BuyerDueDateDisplay = customer.CreditDays > 0
			? $"Due {DateTime.Today.AddDays(customer.CreditDays):dd-MMM-yyyy} ({customer.CreditDays} days credit)"
			: "Cash / immediate";

		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			List<CustomerLicence> licences = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().CustomerLicences
				.AsNoTracking()
				.Where(item => item.CustomerId == customer.Id)
				.OrderBy(item => item.LicenceType)
				.ThenBy(item => item.LicenceNumber)
				.ToListAsync(cancellationToken);
			BuyerDrugLicence = licences.Count == 0
				? string.Empty
				: string.Join("; ", licences.Select(item => $"{item.LicenceType} {item.LicenceNumber}".Trim()));
		}
		catch
		{
			BuyerDrugLicence = string.Empty;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedStockToAddChanged(WholesaleStockChoice? value)
	{
		OnPropertyChanged("ShowStockPickerPlaceholder");
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnPaymentMethodChanged(string value)
	{
		OnPropertyChanged("IsUpiPayment");
		if (!_suppressUpiAutoModal && IsUpiPayment && EstimatedTotal > 0m)
		{
			ShowPaymentQrAsync();
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnCreditLockAlertChanged(string value)
	{
		OnPropertyChanged("HasCreditLockAlert");
	}
}
