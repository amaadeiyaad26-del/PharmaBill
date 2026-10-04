using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class WholesaleAccountsPageViewModel : ObservableObject, ILoadablePage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSession _session;
    private readonly IFilePickerService _filePicker;
    private readonly TabularExportService _exports;

    public WholesaleAccountsPageViewModel(
        IServiceScopeFactory scopeFactory,
        CurrentSession session,
        IFilePickerService filePicker,
        TabularExportService exports)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _filePicker = filePicker;
        _exports = exports;
    }

    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<WholesaleInvoice> UnpaidInvoices { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<LedgerRow> CustomerLedger { get; } = [];
    public ObservableCollection<LedgerRow> SupplierLedger { get; } = [];
    public ObservableCollection<CustomerAgeing> Ageing { get; } = [];
    public ObservableCollection<CashBankBookRow> CashBankBook { get; } = [];
    public ObservableCollection<(Guid SupplierId, string SupplierName, decimal Payable)> SupplierPayables { get; } = [];

    [ObservableProperty] private Customer? _selectedCustomer;
    [ObservableProperty] private WholesaleInvoice? _selectedInvoice;
    [ObservableProperty] private Supplier? _selectedSupplier;
    [ObservableProperty] private string _receiptAmount = string.Empty;
    [ObservableProperty] private string _paymentMethod = "Cash";
    [ObservableProperty] private string _receiptReference = string.Empty;
    [ObservableProperty] private string _chequeNumber = string.Empty;
    [ObservableProperty] private DateTime? _chequeDate = DateTime.Today;
    [ObservableProperty] private string _bankName = string.Empty;
    [ObservableProperty] private string _supplierPaymentAmount = string.Empty;
    [ObservableProperty] private string _supplierPaymentReference = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public string[] PaymentMethods { get; } = ["Cash", "UPI", "Card", "Cheque"];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var customers = await context.Customers.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        Customers.Clear();
        foreach (var customer in customers)
        {
            Customers.Add(customer);
        }

        var suppliers = await context.Suppliers.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        Suppliers.Clear();
        foreach (var supplier in suppliers)
        {
            Suppliers.Add(supplier);
        }

        await ReloadAsync(cancellationToken);
    }

    partial void OnSelectedCustomerChanged(Customer? value)
    {
        if (value is not null)
        {
            _ = LoadCustomerAsync(value.Id);
        }
    }

    partial void OnSelectedSupplierChanged(Supplier? value)
    {
        if (value is not null)
        {
            _ = LoadSupplierAsync(value.Id);
        }
    }

    [RelayCommand]
    private async Task RecordReceiptAsync()
    {
        if (SelectedCustomer is null ||
            !decimal.TryParse(ReceiptAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
        {
            ErrorMessage = "Select a customer and enter a valid receipt amount.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>().RecordReceiptAsync(
                SelectedCustomer.Id,
                SelectedInvoice?.Id,
                amount,
                PaymentMethod,
                ReceiptReference,
                ChequeNumber,
                ChequeDate.HasValue ? DateOnly.FromDateTime(ChequeDate.Value) : null,
                BankName,
                _session.User?.Id ?? throw new UnauthorizedAccessException("Sign in before recording receipts."));
            ReceiptAmount = string.Empty;
            ErrorMessage = string.Empty;
            StatusMessage = "Receipt saved and allocated.";
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task RecordSupplierPaymentAsync()
    {
        if (SelectedSupplier is null ||
            !decimal.TryParse(SupplierPaymentAmount, NumberStyles.Number, CultureInfo.CurrentCulture, out var amount))
        {
            ErrorMessage = "Select a supplier and enter a valid payment amount.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>().RecordSupplierPaymentAsync(
                SelectedSupplier.Id,
                amount,
                PaymentMethod,
                SupplierPaymentReference,
                ChequeNumber,
                ChequeDate.HasValue ? DateOnly.FromDateTime(ChequeDate.Value) : null,
                BankName,
                _session.User?.Id ?? throw new UnauthorizedAccessException("Sign in before recording supplier payments."));
            SupplierPaymentAmount = string.Empty;
            ErrorMessage = string.Empty;
            StatusMessage = "Supplier payment saved.";
            await ReloadAsync();
            await LoadSupplierAsync(SelectedSupplier.Id);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ExportStatementAsync()
    {
        if (SelectedCustomer is null)
        {
            ErrorMessage = "Select a customer before exporting a statement.";
            return;
        }

        var destination = _filePicker.PickExportDestination("pdf", $"Statement-{SelectedCustomer.Name}");
        if (destination is null)
        {
            return;
        }

        var rows = CustomerLedger.Select(row => (IReadOnlyList<string>)
        [
            row.EntryAtUtc == DateTime.MinValue ? "Opening balance" : row.EntryAtUtc.ToLocalTime().ToString("dd-MMM-yyyy"),
            row.EntryType,
            row.ReferenceNo ?? string.Empty,
            row.Debit.ToString("0.00"),
            row.Credit.ToString("0.00"),
            row.Balance.ToString("0.00"),
            row.Notes ?? string.Empty
        ]).ToArray();
        await _exports.ExportAsync(destination, $"Party statement: {SelectedCustomer.Name}",
            ["Date", "Type", "Reference", "Debit", "Credit", "Balance", "Notes"], rows);
        StatusMessage = destination;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void OpenWhatsAppReminder(CustomerAgeing? customer)
    {
        if (customer is null || string.IsNullOrWhiteSpace(customer.Phone))
        {
            ErrorMessage = "The selected party has no phone number for a WhatsApp reminder.";
            return;
        }

        var phone = new string(customer.Phone.Where(char.IsDigit).ToArray());
        if (phone.Length < 8)
        {
            ErrorMessage = "The party phone number is not valid for a WhatsApp reminder.";
            return;
        }

        var message = Uri.EscapeDataString(
            $"Dear {customer.CustomerName}, our records show an outstanding balance of {MoneyFormat.Rupees(customer.Total)}. Please contact us to reconcile your account.");
        Process.Start(new ProcessStartInfo($"https://wa.me/{phone}?text={message}") { UseShellExecute = true });
        ErrorMessage = string.Empty;
    }

    private async Task LoadCustomerAsync(Guid customerId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            var accounts = scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>();
            var invoices = await context.WholesaleInvoices.AsNoTracking()
                .Where(invoice => invoice.CustomerId == customerId && invoice.Status == "Posted")
                .OrderByDescending(invoice => invoice.InvoiceAtUtc)
                .ToListAsync();
            UnpaidInvoices.Clear();
            foreach (var invoice in invoices)
            {
                UnpaidInvoices.Add(invoice);
            }

            CustomerLedger.Clear();
            foreach (var row in await accounts.GetCustomerLedgerAsync(customerId))
            {
                CustomerLedger.Add(row);
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task LoadSupplierAsync(Guid supplierId)
    {
        using var scope = _scopeFactory.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>();
        SupplierLedger.Clear();
        foreach (var row in await accounts.GetSupplierLedgerAsync(supplierId))
        {
            SupplierLedger.Add(row);
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<WholesaleAccountsService>();
        Ageing.Clear();
        foreach (var row in await accounts.GetAgeingAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken))
        {
            Ageing.Add(row);
        }

        CashBankBook.Clear();
        foreach (var row in await accounts.GetCashBankBookAsync(
                     DateTime.UtcNow.Date,
                     DateTime.UtcNow.Date.AddDays(1),
                     cancellationToken))
        {
            CashBankBook.Add(row);
        }

        SupplierPayables.Clear();
        foreach (var payable in await accounts.GetSupplierPayablesAsync(cancellationToken))
        {
            SupplierPayables.Add(payable);
        }
    }
}
