using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
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

	private string _errorMessage = string.Empty;

	private string _statusMessage = string.Empty;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? recordReceiptCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? recordSupplierPaymentCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private AsyncRelayCommand? exportStatementCommand;

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	private RelayCommand<CustomerAgeing?>? openWhatsAppReminderCommand;

	public ObservableCollection<Customer> Customers { get; } = new ObservableCollection<Customer>();

	public ObservableCollection<WholesaleInvoice> UnpaidInvoices { get; } = new ObservableCollection<WholesaleInvoice>();

	public ObservableCollection<Supplier> Suppliers { get; } = new ObservableCollection<Supplier>();

	public ObservableCollection<LedgerRow> CustomerLedger { get; } = new ObservableCollection<LedgerRow>();

	public ObservableCollection<LedgerRow> SupplierLedger { get; } = new ObservableCollection<LedgerRow>();

	public ObservableCollection<CustomerAgeing> Ageing { get; } = new ObservableCollection<CustomerAgeing>();

	public ObservableCollection<CashBankBookRow> CashBankBook { get; } = new ObservableCollection<CashBankBookRow>();

	public ObservableCollection<(Guid SupplierId, string SupplierName, decimal Payable)> SupplierPayables { get; } = new ObservableCollection<(Guid, string, decimal)>();

	public string[] PaymentMethods { get; } = new string[4] { "Cash", "UPI", "Card", "Cheque" };

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
				_supplierPaymentAmount = value;
				OnPropertyChanged(nameof(SupplierPaymentAmount));
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
				_supplierPaymentReference = value;
				OnPropertyChanged(nameof(SupplierPaymentReference));
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

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RecordReceiptCommand => recordReceiptCommand ?? (recordReceiptCommand = new AsyncRelayCommand(RecordReceiptAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand RecordSupplierPaymentCommand => recordSupplierPaymentCommand ?? (recordSupplierPaymentCommand = new AsyncRelayCommand(RecordSupplierPaymentAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IAsyncRelayCommand ExportStatementCommand => exportStatementCommand ?? (exportStatementCommand = new AsyncRelayCommand(ExportStatementAsync));

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.RelayCommandGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public IRelayCommand<CustomerAgeing?> OpenWhatsAppReminderCommand => openWhatsAppReminderCommand ?? (openWhatsAppReminderCommand = new RelayCommand<CustomerAgeing>(OpenWhatsAppReminder));

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
			await scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>().RecordSupplierPaymentAsync(SelectedSupplier.Id, result, PaymentMethod, SupplierPaymentReference, ChequeNumber, ChequeDate.HasValue ? new DateOnly?(DateOnly.FromDateTime(ChequeDate.Value)) : ((DateOnly?)null), BankName, (_session.User ?? throw new UnauthorizedAccessException("Sign in before recording supplier payments.")).Id);
			SupplierPaymentAmount = string.Empty;
			ErrorMessage = string.Empty;
			StatusMessage = "Supplier payment saved.";
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
		SupplierLedger.Clear();
		foreach (LedgerRow item in await requiredService.GetSupplierLedgerAsync(supplierId))
		{
			SupplierLedger.Add(item);
		}
	}

	private async Task ReloadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = _scopeFactory.CreateScope();
		WholesaleAccountsService accounts = scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>();
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
			LoadSupplierAsync(value.Id);
		}
	}
}
