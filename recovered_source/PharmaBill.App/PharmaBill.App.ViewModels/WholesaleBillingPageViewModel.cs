using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
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

	private string _pharmacyName = "Pharmacy";

	private string _upiId = string.Empty;

	private bool _suppressUpiAutoModal;

	[ObservableProperty]
	private Customer? _selectedCustomer;

	[ObservableProperty]
	private WholesaleStockChoice? _selectedStockToAdd;

	[ObservableProperty]
	private WholesaleInvoiceLineDraft? _selectedLine;

	[ObservableProperty]
	private string _invoiceNoPreview = string.Empty;

	[ObservableProperty]
	private bool _isStockChoicesLoading;

	[ObservableProperty]
	private string _paymentMethod = "Cash";

	[ObservableProperty]
	private decimal _paidAmount;

	[ObservableProperty]
	private string _paymentReference = string.Empty;

	[ObservableProperty]
	private bool _confirmNearExpiry;

	[ObservableProperty]
	private bool _overrideCreditOrOverdue;

	[ObservableProperty]
	private string _overrideReason = string.Empty;

	[ObservableProperty]
	private string _transportDetails = string.Empty;

	[ObservableProperty]
	private string _vehicleNumber = string.Empty;

	[ObservableProperty]
	private string _eWayBillNumber = string.Empty;

	[ObservableProperty]
	private string _irn = string.Empty;

	[ObservableProperty]
	private string _notes = string.Empty;

	[ObservableProperty]
	private string _statusMessage = string.Empty;

	[ObservableProperty]
	private string _errorMessage = string.Empty;

	[ObservableProperty]
	private Guid? _lastInvoiceId;

	[ObservableProperty]
	private string _creditLockAlert = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? showPaymentQrCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand? addLineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<WholesaleInvoiceLineDraft?>? removeLineCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? saveInvoiceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportTaxInvoiceCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportDeliveryChallanCommand;

	public string[] PaymentMethods { get; } = new string[5] { "Cash", "UPI / QR", "Card / POS", "Cheque / Bank Transfer", "Credit / Ledger" };

	public bool IsUpiPayment => UpiPaymentPayload.IsUpi(PaymentMethod);

	public ObservableCollection<Customer> Customers { get; } = new ObservableCollection<Customer>();

	public ObservableCollection<WholesaleStockChoice> StockChoices { get; } = new ObservableCollection<WholesaleStockChoice>();

	public ObservableCollection<WholesaleInvoiceLineDraft> Items { get; } = new ObservableCollection<WholesaleInvoiceLineDraft>();

	public bool HasCreditLockAlert => !string.IsNullOrWhiteSpace(CreditLockAlert);

	public bool HasStockChoices => StockChoices.Count > 0;

	public string StockPickerPlaceholder
	{
		get
		{
			if (!IsStockChoicesLoading)
			{
				if (!HasStockChoices)
				{
					return "No stocked batches available";
				}
				return "Select medicine to add…";
			}
			return "Loading stocked batches…";
		}
	}

	public bool ShowStockPickerPlaceholder => (object)SelectedStockToAdd == null;

	public decimal Subtotal => Items.Sum((WholesaleInvoiceLineDraft item) => item.Quantity * item.UnitPrice - item.DiscountAmount);

	public decimal EstimatedTax => Items.Sum((WholesaleInvoiceLineDraft item) => decimal.Round((item.Quantity * item.UnitPrice - item.DiscountAmount) * (item.StockChoice?.GstRate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero));

	public decimal EstimatedTotal => decimal.Round(Subtotal + EstimatedTax, 0, MidpointRounding.AwayFromZero);

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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedCustomer);
				_selectedCustomer = value;
				OnSelectedCustomerChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedCustomer);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedStockToAdd);
				_selectedStockToAdd = value;
				OnSelectedStockToAddChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedStockToAdd);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.SelectedLine);
				_selectedLine = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.SelectedLine);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.InvoiceNoPreview);
				_invoiceNoPreview = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.InvoiceNoPreview);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.IsStockChoicesLoading);
				_isStockChoicesLoading = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.IsStockChoicesLoading);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PaymentMethod);
				_paymentMethod = value;
				OnPaymentMethodChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PaymentMethod);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PaidAmount);
				_paidAmount = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PaidAmount);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.PaymentReference);
				_paymentReference = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.PaymentReference);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.ConfirmNearExpiry);
				_confirmNearExpiry = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.ConfirmNearExpiry);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.OverrideCreditOrOverdue);
				_overrideCreditOrOverdue = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.OverrideCreditOrOverdue);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.OverrideReason);
				_overrideReason = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.OverrideReason);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.TransportDetails);
				_transportDetails = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.TransportDetails);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.VehicleNumber);
				_vehicleNumber = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.VehicleNumber);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.EWayBillNumber);
				_eWayBillNumber = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.EWayBillNumber);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Irn);
				_irn = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Irn);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.Notes);
				_notes = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.Notes);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.StatusMessage);
				_statusMessage = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.StatusMessage);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.LastInvoiceId);
				_lastInvoiceId = value;
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.LastInvoiceId);
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
				OnPropertyChanging(__KnownINotifyPropertyChangingArgs.CreditLockAlert);
				_creditLockAlert = value;
				OnCreditLockAlertChanged(value);
				OnPropertyChanged(__KnownINotifyPropertyChangedArgs.CreditLockAlert);
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ShowPaymentQrCommand => showPaymentQrCommand ?? (showPaymentQrCommand = new AsyncRelayCommand(ShowPaymentQrAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand AddLineCommand => addLineCommand ?? (addLineCommand = new RelayCommand(AddLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<WholesaleInvoiceLineDraft?> RemoveLineCommand => removeLineCommand ?? (removeLineCommand = new RelayCommand<WholesaleInvoiceLineDraft>(RemoveLine));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand SaveInvoiceCommand => saveInvoiceCommand ?? (saveInvoiceCommand = new AsyncRelayCommand(SaveInvoiceAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportTaxInvoiceCommand => exportTaxInvoiceCommand ?? (exportTaxInvoiceCommand = new AsyncRelayCommand(ExportTaxInvoiceAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportDeliveryChallanCommand => exportDeliveryChallanCommand ?? (exportDeliveryChallanCommand = new AsyncRelayCommand(ExportDeliveryChallanAsync));

	public WholesaleBillingPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session, IConfirmationService confirmation, WholesaleInvoiceDocumentService documents, IPaymentQrDialogService paymentQrDialogs, DocumentOutputSettingsStore documentOutputSettings, INavigationService navigation, SettingsPageViewModel settingsPage)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_confirmation = confirmation;
		_documents = documents;
		_paymentQrDialogs = paymentQrDialogs;
		_documentOutputSettings = documentOutputSettings;
		_navigation = navigation;
		_settingsPage = settingsPage;
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
			List<Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
				where drug.IsActive
				select drug).ToListAsync(cancellationToken);
			Guid[] drugIds = drugs.Select((Drug drug) => drug.Id).ToArray();
			List<Batch> batches = await (from batch in context.Batches.AsNoTracking()
				where drugIds.Contains(batch.DrugId)
				select batch).ToListAsync(cancellationToken);
			Guid[] batchIds = batches.Select((Batch batch) => batch.Id).ToArray();
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
				select new WholesaleStockChoice(drug.Id, batch.Id, drug.Name, batch.BatchNo, batch.ExpiryDate, available, batch.Mrp ?? drug.Mrp.GetValueOrDefault(), drug.Schedule, drug.GstRate.GetValueOrDefault())).ToList();
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
			NumberSeriesService series = scope.ServiceProvider.GetRequiredService<NumberSeriesService>();
			PharmacyProfile pharmacyProfile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
			_pharmacyName = pharmacyProfile.Name;
			_upiId = pharmacyProfile.UpiId ?? string.Empty;
			InvoiceNoPreview = await series.PreviewNextAsync(pharmacyProfile.InvoicePrefix, null, cancellationToken);
		}
		finally
		{
			IsStockChoicesLoading = false;
			OnPropertyChanged("StockPickerPlaceholder");
		}
	}

	[RelayCommand]
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

	[RelayCommand]
	private void AddLine()
	{
		WholesaleStockChoice wholesaleStockChoice = SelectedStockToAdd ?? StockChoices.FirstOrDefault();
		if ((object)wholesaleStockChoice == null)
		{
			ErrorMessage = "No stocked medicine batches are available to add.";
			return;
		}
		Items.Add(new WholesaleInvoiceLineDraft
		{
			StockChoice = wholesaleStockChoice,
			Quantity = 1m,
			UnitPrice = wholesaleStockChoice.Mrp
		});
		NotifyTotalsChanged();
	}

	[RelayCommand]
	private void RemoveLine(WholesaleInvoiceLineDraft? line)
	{
		if (line != null)
		{
			Items.Remove(line);
			NotifyTotalsChanged();
		}
	}

	[RelayCommand]
	private async Task SaveInvoiceAsync()
	{
		ErrorMessage = string.Empty;
		AppUser appUser = _session.User ?? throw new UnauthorizedAccessException("Sign in before creating invoices.");
		if (SelectedCustomer == null || Items.Count == 0 || Items.Any((WholesaleInvoiceLineDraft item) => (object)item.StockChoice == null))
		{
			ErrorMessage = "Choose an active customer and at least one stocked item.";
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
			WholesaleInvoiceResult wholesaleInvoiceResult = await scope.ServiceProvider.GetRequiredService<WholesaleInvoiceService>().SaveAsync(new SaveWholesaleInvoiceInput(SelectedCustomer.Id, Items.Select((WholesaleInvoiceLineDraft item) => new WholesaleInvoiceLineInput(item.StockChoice.DrugId, item.StockChoice.BatchId, item.Quantity, item.FreeQuantity, item.UnitPrice, item.DiscountAmount)).ToArray(), PaidAmount, UpiPaymentPayload.NormalizeMethod(PaymentMethod), PaymentReference, ConfirmNearExpiry, 90, OverrideCreditOrOverdue, OverrideReason, TransportDetails, VehicleNumber, EWayBillNumber, Irn, Notes), appUser.Id, appUser.Role);
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

	[RelayCommand]
	private async Task ExportTaxInvoiceAsync()
	{
		if (LastInvoiceId.HasValue)
		{
			StatusMessage = await _documents.ExportAsync(LastInvoiceId.Value, deliveryChallan: false);
		}
	}

	[RelayCommand]
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
		RefreshCreditGateAsync();
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
