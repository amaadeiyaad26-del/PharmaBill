using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public class WholesaleAccountsPageViewModel : ObservableObject, ILoadablePage
{
	private readonly IServiceScopeFactory _scopeFactory;

	private readonly CurrentSession _session;

	private readonly IFilePickerService _filePicker;

	private readonly TabularExportService _exports;

	private Customer? _selectedCustomer;

	private WholesaleInvoice? _selectedInvoice;

	private Supplier? _selectedSupplier;

	private string _receiptAmount = string.Empty;

	private string _paymentMethod = "Cash";

	private string _receiptReference = string.Empty;

	private string _chequeNumber = string.Empty;

	private DateTime? _chequeDate = DateTime.Today;

	private string _bankName = string.Empty;

	private string _supplierPaymentAmount = string.Empty;

	private string _supplierPaymentReference = string.Empty;

	private string _supplierPaymentMethod = "Cash";

	private DateTime? _supplierPaymentDate = DateTime.Today;

	private string _errorMessage = string.Empty;

	private string _statusMessage = string.Empty;

	private bool _isRetailMode;

	private decimal _currentSupplierOutstanding;

	private DateTime? _ledgerFromDate;

	private DateTime? _ledgerToDate = DateTime.Today;

	private string _ledgerSearchText = string.Empty;

	private string _ledgerQuickFilter = "All";

	private decimal _totalPayables;

	private decimal _paidThisMonth;

	private int _activeDistributors;

	private decimal _overduePayables;

	private readonly List<LedgerRow> _supplierLedgerSource = new();

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? recordReceiptCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? recordSupplierPaymentCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportStatementCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<CustomerAgeing?>? openWhatsAppReminderCommand;

	private RelayCommand? autoFillOutstandingCommand;

	private AsyncRelayCommand? refreshLedgerCommand;

	private AsyncRelayCommand? exportSupplierPdfCommand;

	private AsyncRelayCommand? exportSupplierExcelCommand;

	private AsyncRelayCommand? printSupplierStatementCommand;

	private RelayCommand<string>? setLedgerQuickFilterCommand;

	public ObservableCollection<Customer> Customers { get; } = new ObservableCollection<Customer>();

	public ObservableCollection<WholesaleInvoice> UnpaidInvoices { get; } = new ObservableCollection<WholesaleInvoice>();

	public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();

	public ObservableCollection<LedgerRow> CustomerLedger { get; } = new ObservableCollection<LedgerRow>();

	public ObservableCollection<LedgerRow> SupplierLedger { get; } = new ObservableCollection<LedgerRow>();

	public ObservableCollection<SupplierLedgerDisplayRow> FilteredSupplierLedger { get; } = new ObservableCollection<SupplierLedgerDisplayRow>();

	public ObservableCollection<CustomerAgeing> Ageing { get; } = new ObservableCollection<CustomerAgeing>();

	public ObservableCollection<CashBankBookRow> CashBankBook { get; } = new ObservableCollection<CashBankBookRow>();

	public ObservableCollection<(Guid SupplierId, string SupplierName, decimal Payable)> SupplierPayables { get; } = new ObservableCollection<(Guid, string, decimal)>();

	public ObservableCollection<SupplierPayableAgeingRow> SupplierPayablesAgeing { get; } = new ObservableCollection<SupplierPayableAgeingRow>();

	public string[] PaymentMethods { get; } = new string[4] { "Cash", "UPI", "Card", "Cheque" };

	public string[] SupplierPaymentMethods { get; } = new string[5] { "Cash", "UPI", "NEFT", "Cheque", "Bank" };

	/// <summary>Alias for bindings that expect PaymentModes.</summary>
	public string[] PaymentModes => SupplierPaymentMethods;

	public string PageTitle => "Accounts & Supplier Ledgers";

	public string PageSubtitle => _isRetailMode
		? "Track what you owe medicine distributors, and collect dues from retail credit customers."
		: "Supplier payables and wholesale buyer receivables.";

	public string CustomerAccountsTabHeader => _isRetailMode
		? "👤 Retail Customer Accounts & Dues"
		: "👤 Wholesale Buyer Ledgers & Receivables";

	public string CustomerPaymentSectionTitle => _isRetailMode
		? "Customer Dues / Receive Payment"
		: "Record receipt (buyer payment — choose invoice or leave blank for on-account)";

	public string CustomerPaymentSaveLabel => _isRetailMode ? "Receive Payment" : "Save receipt";

	public string AgeingPartyHeader => _isRetailMode ? "Customer / Patient" : "Buyer";

	public bool IsRetailMode => _isRetailMode;

	public bool IsWholesaleMode => !_isRetailMode;

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
	public WholesaleInvoice? SelectedInvoice
	{
		get
		{
			return _selectedInvoice;
		}
		set
		{
			if (!EqualityComparer<WholesaleInvoice>.Default.Equals(_selectedInvoice, value))
			{
				OnPropertyChanging(nameof(SelectedInvoice));
				_selectedInvoice = value;
				OnPropertyChanged(nameof(SelectedInvoice));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public Supplier? SelectedSupplier
	{
		get
		{
			return _selectedSupplier;
		}
		set
		{
			if (!EqualityComparer<Supplier>.Default.Equals(_selectedSupplier, value))
			{
				OnPropertyChanging(nameof(SelectedSupplier));
				_selectedSupplier = value;
				OnSelectedSupplierChanged(value);
				OnPropertyChanged(nameof(SelectedSupplier));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ReceiptAmount
	{
		get
		{
			return _receiptAmount;
		}
		[MemberNotNull("_receiptAmount")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_receiptAmount, value))
			{
				OnPropertyChanging(nameof(ReceiptAmount));
				_receiptAmount = value;
				OnPropertyChanged(nameof(ReceiptAmount));
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
				OnPropertyChanged(nameof(PaymentMethod));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string ReceiptReference
	{
		get
		{
			return _receiptReference;
		}
		[MemberNotNull("_receiptReference")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_receiptReference, value))
			{
				OnPropertyChanging(nameof(ReceiptReference));
				_receiptReference = value;
				OnPropertyChanged(nameof(ReceiptReference));
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
	public DateTime? ChequeDate
	{
		get
		{
			return _chequeDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_chequeDate, value))
			{
				OnPropertyChanging(nameof(ChequeDate));
				_chequeDate = value;
				OnPropertyChanged(nameof(ChequeDate));
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
				OnPropertyChanging(nameof(BankName));
				_bankName = value;
				OnPropertyChanged(nameof(BankName));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SupplierPaymentAmount
	{
		get
		{
			return _supplierPaymentAmount;
		}
		[MemberNotNull("_supplierPaymentAmount")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_supplierPaymentAmount, value))
			{
				OnPropertyChanging(nameof(SupplierPaymentAmount));
				OnPropertyChanging(nameof(AmountPaid));
				_supplierPaymentAmount = value;
				OnPropertyChanged(nameof(SupplierPaymentAmount));
				OnPropertyChanged(nameof(AmountPaid));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public string SupplierPaymentReference
	{
		get
		{
			return _supplierPaymentReference;
		}
		[MemberNotNull("_supplierPaymentReference")]
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_supplierPaymentReference, value))
			{
				OnPropertyChanging(nameof(SupplierPaymentReference));
				OnPropertyChanging(nameof(ReferenceNo));
				_supplierPaymentReference = value;
				OnPropertyChanged(nameof(SupplierPaymentReference));
				OnPropertyChanged(nameof(ReferenceNo));
			}
		}
	}

	public string SupplierPaymentMethod
	{
		get
		{
			return _supplierPaymentMethod;
		}
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_supplierPaymentMethod, value))
			{
				_supplierPaymentMethod = value;
				OnPropertyChanged(nameof(SupplierPaymentMethod));
			}
		}
	}

	public DateTime? SupplierPaymentDate
	{
		get
		{
			return _supplierPaymentDate;
		}
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_supplierPaymentDate, value))
			{
				_supplierPaymentDate = value;
				OnPropertyChanged(nameof(SupplierPaymentDate));
				OnPropertyChanged(nameof(PaymentDate));
			}
		}
	}

	/// <summary>Alias for AmountPaid bindings.</summary>
	public string AmountPaid
	{
		get => SupplierPaymentAmount;
		set => SupplierPaymentAmount = value;
	}

	/// <summary>Alias for ReferenceNo bindings.</summary>
	public string ReferenceNo
	{
		get => SupplierPaymentReference;
		set => SupplierPaymentReference = value;
	}

	/// <summary>Alias for PaymentDate bindings.</summary>
	public DateTime? PaymentDate
	{
		get => SupplierPaymentDate;
		set => SupplierPaymentDate = value;
	}

	public decimal CurrentSupplierOutstanding
	{
		get => _currentSupplierOutstanding;
		private set
		{
			if (_currentSupplierOutstanding != value)
			{
				_currentSupplierOutstanding = value;
				OnPropertyChanged(nameof(CurrentSupplierOutstanding));
				OnPropertyChanged(nameof(CurrentSupplierOutstandingText));
				OnPropertyChanged(nameof(HasSupplierOutstanding));
			}
		}
	}

	public string CurrentSupplierOutstandingText =>
		"Current Dues: " + MoneyFormat.Rupees(CurrentSupplierOutstanding);

	public bool HasSupplierOutstanding => CurrentSupplierOutstanding > 0m;

	public DateTime? LedgerFromDate
	{
		get => _ledgerFromDate;
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_ledgerFromDate, value))
			{
				_ledgerFromDate = value;
				OnPropertyChanged(nameof(LedgerFromDate));
				ApplySupplierLedgerFilter();
			}
		}
	}

	public DateTime? LedgerToDate
	{
		get => _ledgerToDate;
		set
		{
			if (!EqualityComparer<DateTime?>.Default.Equals(_ledgerToDate, value))
			{
				_ledgerToDate = value;
				OnPropertyChanged(nameof(LedgerToDate));
				ApplySupplierLedgerFilter();
			}
		}
	}

	public string LedgerSearchText
	{
		get => _ledgerSearchText;
		set
		{
			if (!EqualityComparer<string>.Default.Equals(_ledgerSearchText, value))
			{
				_ledgerSearchText = value ?? string.Empty;
				OnPropertyChanged(nameof(LedgerSearchText));
				ApplySupplierLedgerFilter();
			}
		}
	}

	public string LedgerQuickFilter
	{
		get => _ledgerQuickFilter;
		private set
		{
			if (!EqualityComparer<string>.Default.Equals(_ledgerQuickFilter, value))
			{
				_ledgerQuickFilter = value;
				OnPropertyChanged(nameof(LedgerQuickFilter));
			}
		}
	}

	public decimal TotalPayables
	{
		get => _totalPayables;
		private set
		{
			if (_totalPayables != value)
			{
				_totalPayables = value;
				OnPropertyChanged(nameof(TotalPayables));
				OnPropertyChanged(nameof(TotalPayablesText));
				OnPropertyChanged(nameof(TotalPayablesAmount));
			}
		}
	}

	public string TotalPayablesText => MoneyFormat.Rupees(TotalPayables);

	/// <summary>Alias for KPI binding TotalPayablesAmount.</summary>
	public string TotalPayablesAmount => TotalPayablesText;

	public decimal PaidThisMonth
	{
		get => _paidThisMonth;
		private set
		{
			if (_paidThisMonth != value)
			{
				_paidThisMonth = value;
				OnPropertyChanged(nameof(PaidThisMonth));
				OnPropertyChanged(nameof(PaidThisMonthText));
				OnPropertyChanged(nameof(PaidThisMonthAmount));
			}
		}
	}

	public string PaidThisMonthText => MoneyFormat.Rupees(PaidThisMonth);

	/// <summary>Alias for KPI binding PaidThisMonthAmount.</summary>
	public string PaidThisMonthAmount => PaidThisMonthText;

	public int ActiveDistributors
	{
		get => _activeDistributors;
		private set
		{
			if (_activeDistributors != value)
			{
				_activeDistributors = value;
				OnPropertyChanged(nameof(ActiveDistributors));
				OnPropertyChanged(nameof(ActiveDistributorsCount));
			}
		}
	}

	/// <summary>Alias for KPI binding ActiveDistributorsCount.</summary>
	public int ActiveDistributorsCount => ActiveDistributors;

	public decimal OverduePayables
	{
		get => _overduePayables;
		private set
		{
			if (_overduePayables != value)
			{
				_overduePayables = value;
				OnPropertyChanged(nameof(OverduePayables));
				OnPropertyChanged(nameof(OverduePayablesText));
				OnPropertyChanged(nameof(HasOverduePayables));
			}
		}
	}

	public string OverduePayablesText => MoneyFormat.Rupees(OverduePayables);

	public bool HasOverduePayables => OverduePayables > 0m;

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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RecordReceiptCommand => recordReceiptCommand ?? (recordReceiptCommand = new AsyncRelayCommand(RecordReceiptAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RecordSupplierPaymentCommand => recordSupplierPaymentCommand ?? (recordSupplierPaymentCommand = new AsyncRelayCommand(RecordSupplierPaymentAsync));

	/// <summary>Alias for SavePaymentCommand bindings.</summary>
	public IAsyncRelayCommand SavePaymentCommand => RecordSupplierPaymentCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportStatementCommand => exportStatementCommand ?? (exportStatementCommand = new AsyncRelayCommand(ExportStatementAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<CustomerAgeing?> OpenWhatsAppReminderCommand => openWhatsAppReminderCommand ?? (openWhatsAppReminderCommand = new RelayCommand<CustomerAgeing>(OpenWhatsAppReminder));

	public IRelayCommand AutoFillOutstandingCommand => autoFillOutstandingCommand ?? (autoFillOutstandingCommand = new RelayCommand(AutoFillOutstanding));

	public IAsyncRelayCommand RefreshLedgerCommand => refreshLedgerCommand ?? (refreshLedgerCommand = new AsyncRelayCommand(RefreshLedgerAsync));

	public IAsyncRelayCommand ExportSupplierPdfCommand => exportSupplierPdfCommand ?? (exportSupplierPdfCommand = new AsyncRelayCommand(async () => { await ExportSupplierStatementAsync("pdf"); }));

	public IAsyncRelayCommand ExportSupplierExcelCommand => exportSupplierExcelCommand ?? (exportSupplierExcelCommand = new AsyncRelayCommand(async () => { await ExportSupplierStatementAsync("xlsx"); }));

	public IAsyncRelayCommand PrintSupplierStatementCommand => printSupplierStatementCommand ?? (printSupplierStatementCommand = new AsyncRelayCommand(PrintSupplierStatementAsync));

	public IRelayCommand<string> SetLedgerQuickFilterCommand => setLedgerQuickFilterCommand ?? (setLedgerQuickFilterCommand = new RelayCommand<string>(SetLedgerQuickFilter));

	public WholesaleAccountsPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session, IFilePickerService filePicker, TabularExportService exports)
	{
		_scopeFactory = scopeFactory;
		_session = session;
		_filePicker = filePicker;
		_exports = exports;
	}

	public async Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		BusinessMode mode = await context.PharmacyProfiles.AsNoTracking().Select((PharmacyProfile profile) => profile.BusinessMode).FirstOrDefaultAsync(cancellationToken);
		_isRetailMode = mode == BusinessMode.Retail;
		OnPropertyChanged(nameof(IsRetailMode));
		OnPropertyChanged(nameof(IsWholesaleMode));
		OnPropertyChanged(nameof(PageSubtitle));
		OnPropertyChanged(nameof(CustomerAccountsTabHeader));
		OnPropertyChanged(nameof(CustomerPaymentSectionTitle));
		OnPropertyChanged(nameof(CustomerPaymentSaveLabel));
		OnPropertyChanged(nameof(AgeingPartyHeader));
		List<Customer> list = await (from item in context.Customers.AsNoTracking()
			orderby item.Name
			select item).ToListAsync(cancellationToken);
		Customers.Clear();
		foreach (Customer item in list)
		{
			Customers.Add(item);
		}
		List<Supplier> list2 = await (from item in context.Suppliers.AsNoTracking()
			orderby item.Name
			select item).ToListAsync(cancellationToken);
		Suppliers.Clear();
		foreach (Supplier item2 in list2)
		{
			Suppliers.Add(item2);
		}
		await ReloadAsync(cancellationToken);
	}

	private async Task RecordReceiptAsync()
	{
		if (SelectedCustomer == null || !decimal.TryParse(ReceiptAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var result))
		{
			ErrorMessage = "Select a customer and enter a valid receipt amount.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			await scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>().RecordReceiptAsync(SelectedCustomer.Id, SelectedInvoice?.Id, result, PaymentMethod, ReceiptReference, ChequeNumber, ChequeDate.HasValue ? new DateOnly?(DateOnly.FromDateTime(ChequeDate.Value)) : ((DateOnly?)null), BankName, (_session.User ?? throw new UnauthorizedAccessException("Sign in before recording receipts.")).Id);
			ReceiptAmount = string.Empty;
			ErrorMessage = string.Empty;
			StatusMessage = "Receipt saved and allocated.";
			await ReloadAsync();
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is UnauthorizedAccessException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task RecordSupplierPaymentAsync()
	{
		if (SelectedSupplier == null || !decimal.TryParse(SupplierPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var result))
		{
			ErrorMessage = "Select a supplier and enter a valid payment amount.";
			return;
		}
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			DateOnly? paymentDate = SupplierPaymentDate.HasValue ? new DateOnly?(DateOnly.FromDateTime(SupplierPaymentDate.Value)) : (ChequeDate.HasValue ? new DateOnly?(DateOnly.FromDateTime(ChequeDate.Value)) : null);
			await scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>().RecordSupplierPaymentAsync(SelectedSupplier.Id, result, SupplierPaymentMethod, SupplierPaymentReference, ChequeNumber, paymentDate, BankName, (_session.User ?? throw new UnauthorizedAccessException("Sign in before recording supplier payments.")).Id);
			SupplierPaymentAmount = string.Empty;
			ErrorMessage = string.Empty;
			StatusMessage = "Supplier payment (outward) saved.";
			await ReloadAsync();
			await LoadSupplierAsync(SelectedSupplier.Id);
		}
		catch (Exception ex) when ((ex is InvalidOperationException || ex is UnauthorizedAccessException) ? true : false)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task ExportStatementAsync()
	{
		if (SelectedCustomer == null)
		{
			ErrorMessage = "Select a customer before exporting a statement.";
			return;
		}
		string destination = _filePicker.PickExportDestination("pdf", "Statement-" + SelectedCustomer.Name);
		if (destination != null)
		{
			IReadOnlyList<string>[] rows = ((IEnumerable<LedgerRow>)CustomerLedger).Select((Func<LedgerRow, IReadOnlyList<string>>)((LedgerRow row) => new _003C_003Ez__ReadOnlyArray<string>(new string[7]
			{
				(row.EntryAtUtc == DateTime.MinValue) ? "Opening balance" : row.EntryAtUtc.ToLocalTime().ToString("dd-MMM-yyyy"),
				row.EntryType,
				row.ReferenceNo ?? string.Empty,
				row.Debit.ToString("0.00"),
				row.Credit.ToString("0.00"),
				row.Balance.ToString("0.00"),
				row.Notes ?? string.Empty
			}))).ToArray();
			await _exports.ExportAsync(destination, "Party statement: " + SelectedCustomer.Name, new _003C_003Ez__ReadOnlyArray<string>(new string[7] { "Date", "Type", "Reference", "Debit", "Credit", "Balance", "Notes" }), rows);
			StatusMessage = destination;
			ErrorMessage = string.Empty;
		}
	}

	private void OpenWhatsAppReminder(CustomerAgeing? customer)
	{
		if ((object)customer == null || string.IsNullOrWhiteSpace(customer.Phone))
		{
			ErrorMessage = "The selected party has no phone number for a WhatsApp reminder.";
			return;
		}
		string text = new string(customer.Phone.Where(char.IsDigit).ToArray());
		if (text.Length < 8)
		{
			ErrorMessage = "The party phone number is not valid for a WhatsApp reminder.";
			return;
		}
		string text2 = Uri.EscapeDataString($"Dear {customer.CustomerName}, our records show an outstanding balance of {MoneyFormat.Rupees(customer.Total)}. Please contact us to reconcile your account.");
		Process.Start(new ProcessStartInfo("https://wa.me/" + text + "?text=" + text2)
		{
			UseShellExecute = true
		});
		ErrorMessage = string.Empty;
	}

	private async Task LoadCustomerAsync(Guid customerId)
	{
		try
		{
			using IServiceScope scope = _scopeFactory.CreateScope();
			PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			WholesaleAccountsService accounts = scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>();
			List<WholesaleInvoice> list = await (from invoice in requiredService.WholesaleInvoices.AsNoTracking()
				where invoice.CustomerId == customerId && invoice.Status == "Posted"
				orderby invoice.InvoiceAtUtc descending
				select invoice).ToListAsync();
			UnpaidInvoices.Clear();
			foreach (WholesaleInvoice item in list)
			{
				UnpaidInvoices.Add(item);
			}
			CustomerLedger.Clear();
			foreach (LedgerRow item2 in await accounts.GetCustomerLedgerAsync(customerId))
			{
				CustomerLedger.Add(item2);
			}
		}
		catch (Exception ex)
		{
			ErrorMessage = ex.Message;
		}
	}

	private async Task LoadSupplierAsync(Guid supplierId)
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		WholesaleAccountsService requiredService = scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>();
		IReadOnlyList<LedgerRow> rows = await requiredService.GetSupplierLedgerAsync(supplierId);
		SupplierLedger.Clear();
		_supplierLedgerSource.Clear();
		foreach (LedgerRow item in rows)
		{
			SupplierLedger.Add(item);
			_supplierLedgerSource.Add(item);
		}

		CurrentSupplierOutstanding = rows.Count == 0
			? 0m
			: decimal.Round(Math.Max(0m, rows[^1].Balance), 2, MidpointRounding.AwayFromZero);
		ApplySupplierLedgerFilter();
	}

	private async Task ReloadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		WholesaleAccountsService accounts = scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		Ageing.Clear();
		foreach (CustomerAgeing item in await accounts.GetAgeingAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken))
		{
			Ageing.Add(item);
		}
		CashBankBook.Clear();
		foreach (CashBankBookRow item2 in await accounts.GetCashBankBookAsync(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1.0), cancellationToken))
		{
			CashBankBook.Add(item2);
		}
		SupplierPayables.Clear();
		foreach (var item3 in await accounts.GetSupplierPayablesAsync(cancellationToken))
		{
			SupplierPayables.Add(item3);
		}

		TotalPayables = decimal.Round(SupplierPayables.Sum(item => item.Payable), 2, MidpointRounding.AwayFromZero);
		ActiveDistributors = SupplierPayables.Count(item => item.Payable > 0m);

		DateTime monthStartUtc = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1, 0, 0, 0, DateTimeKind.Utc);
		PaidThisMonth = decimal.Round(
			await context.SupplierLedgerEntries.AsNoTracking()
				.Where(entry => entry.EntryType == LedgerEntryTypes.SupplierPayment && entry.EntryAtUtc >= monthStartUtc)
				.SumAsync(entry => (decimal?)entry.Debit, cancellationToken) ?? 0m,
			2,
			MidpointRounding.AwayFromZero);

		await RebuildSupplierPayablesAgeingAsync(context, cancellationToken);
		if (SelectedSupplier != null)
		{
			await LoadSupplierAsync(SelectedSupplier.Id);
		}
	}

	private async Task RebuildSupplierPayablesAgeingAsync(PharmaBillDbContext context, CancellationToken cancellationToken)
	{
		SupplierPayablesAgeing.Clear();
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<(Guid SupplierId, DateOnly InvoiceDate, decimal TotalAmount)> invoices = (await context.PurchaseInvoices.AsNoTracking()
			.Select(invoice => new { invoice.SupplierId, invoice.InvoiceDate, invoice.TotalAmount })
			.ToListAsync(cancellationToken))
			.Select(invoice => (invoice.SupplierId, invoice.InvoiceDate, invoice.TotalAmount))
			.ToList();
		Dictionary<Guid, decimal> paymentsBySupplier = await context.SupplierLedgerEntries.AsNoTracking()
			.Where(entry => entry.EntryType == LedgerEntryTypes.SupplierPayment || entry.EntryType == LedgerEntryTypes.PurchaseReturn)
			.GroupBy(entry => entry.SupplierId)
			.Select(group => new { SupplierId = group.Key, Paid = group.Sum(entry => entry.Debit) })
			.ToDictionaryAsync(item => item.SupplierId, item => item.Paid, cancellationToken);
		Dictionary<Guid, string> names = Suppliers.ToDictionary(supplier => supplier.Id, supplier => supplier.Name);
		decimal overdue = 0m;

		foreach (IGrouping<Guid, (Guid SupplierId, DateOnly InvoiceDate, decimal TotalAmount)> group in invoices.GroupBy(invoice => invoice.SupplierId))
		{
			Guid supplierId = group.Key;
			decimal remainingPayment = paymentsBySupplier.GetValueOrDefault(supplierId);
			decimal bucket0 = 0m;
			decimal bucket31 = 0m;
			decimal bucket61 = 0m;
			decimal bucket90 = 0m;
			foreach ((Guid SupplierId, DateOnly InvoiceDate, decimal TotalAmount) invoice in group.OrderBy(item => item.InvoiceDate))
			{
				decimal bill = decimal.Round(invoice.TotalAmount, 2, MidpointRounding.AwayFromZero);
				decimal applied = Math.Min(bill, remainingPayment);
				remainingPayment = decimal.Round(remainingPayment - applied, 2, MidpointRounding.AwayFromZero);
				decimal open = decimal.Round(bill - applied, 2, MidpointRounding.AwayFromZero);
				if (open <= 0m)
				{
					continue;
				}

				int ageDays = today.DayNumber - invoice.InvoiceDate.DayNumber;
				if (ageDays <= 30)
				{
					bucket0 += open;
				}
				else if (ageDays <= 60)
				{
					bucket31 += open;
				}
				else if (ageDays <= 90)
				{
					bucket61 += open;
				}
				else
				{
					bucket90 += open;
				}
			}

			decimal total = decimal.Round(bucket0 + bucket31 + bucket61 + bucket90, 2, MidpointRounding.AwayFromZero);
			if (total <= 0m)
			{
				continue;
			}

			overdue += bucket61 + bucket90;
			SupplierPayablesAgeing.Add(new SupplierPayableAgeingRow
			{
				SupplierId = supplierId,
				SupplierName = names.GetValueOrDefault(supplierId, "Supplier"),
				Current0To30 = decimal.Round(bucket0, 2, MidpointRounding.AwayFromZero),
				Days31To60 = decimal.Round(bucket31, 2, MidpointRounding.AwayFromZero),
				Days61To90 = decimal.Round(bucket61, 2, MidpointRounding.AwayFromZero),
				Over90 = decimal.Round(bucket90, 2, MidpointRounding.AwayFromZero),
				Total = total
			});
		}

		OverduePayables = decimal.Round(overdue, 2, MidpointRounding.AwayFromZero);
	}

	private void AutoFillOutstanding()
	{
		if (CurrentSupplierOutstanding <= 0m)
		{
			ErrorMessage = "No outstanding dues for the selected supplier.";
			return;
		}

		SupplierPaymentAmount = CurrentSupplierOutstanding.ToString("0.00", CultureInfo.CurrentCulture);
		ErrorMessage = string.Empty;
		StatusMessage = "Amount filled with outstanding balance " + MoneyFormat.Rupees(CurrentSupplierOutstanding) + ".";
	}

	private async Task RefreshLedgerAsync()
	{
		if (SelectedSupplier == null)
		{
			ErrorMessage = "Select a supplier to refresh the ledger.";
			return;
		}

		await LoadSupplierAsync(SelectedSupplier.Id);
		StatusMessage = "Supplier ledger refreshed.";
		ErrorMessage = string.Empty;
	}

	private void SetLedgerQuickFilter(string? filter)
	{
		string key = string.IsNullOrWhiteSpace(filter) ? "All" : filter.Trim();
		LedgerQuickFilter = key;
		DateTime today = DateTime.Today;
		switch (key)
		{
			case "Today":
				LedgerFromDate = today;
				LedgerToDate = today;
				break;
			case "This Month":
				LedgerFromDate = new DateTime(today.Year, today.Month, 1);
				LedgerToDate = today;
				break;
			case "Last 30 Days":
				LedgerFromDate = today.AddDays(-30);
				LedgerToDate = today;
				break;
			default:
				LedgerFromDate = null;
				LedgerToDate = today;
				LedgerQuickFilter = "All";
				break;
		}

		ApplySupplierLedgerFilter();
	}

	private void ApplySupplierLedgerFilter()
	{
		FilteredSupplierLedger.Clear();
		string search = LedgerSearchText.Trim();
		DateTime? fromLocal = LedgerFromDate?.Date;
		DateTime? toLocal = LedgerToDate?.Date;
		foreach (LedgerRow row in _supplierLedgerSource)
		{
			DateTime local = row.EntryAtUtc == DateTime.MinValue ? DateTime.MinValue : row.EntryAtUtc.ToLocalTime().Date;
			if (fromLocal.HasValue && local != DateTime.MinValue && local < fromLocal.Value)
			{
				continue;
			}

			if (toLocal.HasValue && local != DateTime.MinValue && local > toLocal.Value)
			{
				continue;
			}

			if (search.Length > 0)
			{
				bool match = (row.ReferenceNo?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
					|| (row.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
					|| row.EntryType.Contains(search, StringComparison.OrdinalIgnoreCase);
				if (!match)
				{
					continue;
				}
			}

			FilteredSupplierLedger.Add(SupplierLedgerDisplayRow.From(row));
		}
	}

	private async Task<string?> ExportSupplierStatementAsync(string extension)
	{
		if (SelectedSupplier == null)
		{
			ErrorMessage = "Select a supplier before exporting a statement.";
			return null;
		}

		string? destination = _filePicker.PickExportDestination(extension, "SupplierStatement-" + SelectedSupplier.Name);
		if (destination == null)
		{
			return null;
		}

		IReadOnlyList<string>[] rows = FilteredSupplierLedger.Select(row => (IReadOnlyList<string>)new string[7]
		{
			row.DateText,
			row.EntryTypeLabel,
			row.ReferenceNo ?? string.Empty,
			row.Debit.ToString("0.00", CultureInfo.InvariantCulture),
			row.Credit.ToString("0.00", CultureInfo.InvariantCulture),
			row.Balance.ToString("0.00", CultureInfo.InvariantCulture),
			row.Notes ?? string.Empty
		}).ToArray();
		await _exports.ExportAsync(
			destination,
			"Supplier statement: " + SelectedSupplier.Name + " · Outstanding " + MoneyFormat.Rupees(CurrentSupplierOutstanding),
			new string[7] { "Date", "Type", "Reference", "Debit", "Credit", "Balance", "Notes" },
			rows);
		StatusMessage = destination;
		ErrorMessage = string.Empty;
		return destination;
	}

	private async Task PrintSupplierStatementAsync()
	{
		string? path = await ExportSupplierStatementAsync("pdf");
		if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
		{
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedCustomerChanged(Customer? value)
	{
		if (value != null)
		{
			LoadCustomerAsync(value.Id);
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	private void OnSelectedSupplierChanged(Supplier? value)
	{
		if (value != null)
		{
			_ = LoadSupplierAsync(value.Id);
		}
		else
		{
			CurrentSupplierOutstanding = 0m;
			_supplierLedgerSource.Clear();
			SupplierLedger.Clear();
			FilteredSupplierLedger.Clear();
		}
	}
}
