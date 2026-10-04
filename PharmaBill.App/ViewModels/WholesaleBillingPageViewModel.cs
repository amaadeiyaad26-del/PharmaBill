using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed record WholesaleStockChoice(
    Guid DrugId,
    Guid BatchId,
    string DrugName,
    string BatchNo,
    DateOnly? ExpiryDate,
    decimal Available,
    decimal Mrp,
    string? Schedule,
    decimal GstRate);

public partial class WholesaleInvoiceLineDraft : ObservableObject
{
    [ObservableProperty] private WholesaleStockChoice? _stockChoice;
    [ObservableProperty] private decimal _quantity = 1m;
    [ObservableProperty] private decimal _freeQuantity;
    [ObservableProperty] private decimal _unitPrice;
    [ObservableProperty] private decimal _discountAmount;

    partial void OnStockChoiceChanged(WholesaleStockChoice? value)
    {
        if (value is not null)
        {
            UnitPrice = value.Mrp;
        }
    }
}

public partial class WholesaleBillingPageViewModel : ObservableObject, ILoadablePage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSession _session;
    private readonly IConfirmationService _confirmation;
    private readonly WholesaleInvoiceDocumentService _documents;

    public WholesaleBillingPageViewModel(
        IServiceScopeFactory scopeFactory,
        CurrentSession session,
        IConfirmationService confirmation,
        WholesaleInvoiceDocumentService documents)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _confirmation = confirmation;
        _documents = documents;
        Items.CollectionChanged += OnItemsChanged;
    }

    public string[] PaymentMethods { get; } = ["Cash", "UPI", "Card", "Cheque"];
    public ObservableCollection<Customer> Customers { get; } = [];
    public ObservableCollection<WholesaleStockChoice> StockChoices { get; } = [];
    public ObservableCollection<WholesaleInvoiceLineDraft> Items { get; } = [];

    [ObservableProperty] private Customer? _selectedCustomer;
    [ObservableProperty] private WholesaleInvoiceLineDraft? _selectedLine;
    [ObservableProperty] private string _invoiceNoPreview = string.Empty;
    [ObservableProperty] private string _paymentMethod = "Cash";
    [ObservableProperty] private decimal _paidAmount;
    [ObservableProperty] private string _paymentReference = string.Empty;
    [ObservableProperty] private bool _confirmNearExpiry;
    [ObservableProperty] private bool _overrideCreditOrOverdue;
    [ObservableProperty] private string _overrideReason = string.Empty;
    [ObservableProperty] private string _transportDetails = string.Empty;
    [ObservableProperty] private string _vehicleNumber = string.Empty;
    [ObservableProperty] private string _eWayBillNumber = string.Empty;
    [ObservableProperty] private string _irn = string.Empty;
    [ObservableProperty] private string _notes = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private Guid? _lastInvoiceId;

    public decimal Subtotal => Items.Sum(item => item.Quantity * item.UnitPrice - item.DiscountAmount);
    public decimal EstimatedTax => Items.Sum(item =>
        decimal.Round((item.Quantity * item.UnitPrice - item.DiscountAmount) *
                     (item.StockChoice?.GstRate ?? 0m) / 100m, 2, MidpointRounding.AwayFromZero));
    public decimal EstimatedTotal => decimal.Round(Subtotal + EstimatedTax, 0, MidpointRounding.AwayFromZero);

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (WholesaleInvoiceLineDraft item in e.OldItems)
            {
                item.PropertyChanged -= OnLinePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (WholesaleInvoiceLineDraft item in e.NewItems)
            {
                item.PropertyChanged += OnLinePropertyChanged;
            }
        }

        NotifyTotalsChanged();
    }

    private void OnLinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        NotifyTotalsChanged();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var customers = await context.Customers.AsNoTracking()
            .Where(customer => customer.IsActive)
            .OrderBy(customer => customer.Name)
            .ToListAsync(cancellationToken);
        Customers.Clear();
        foreach (var customer in customers)
        {
            Customers.Add(customer);
        }

        var drugs = await context.Drugs.AsNoTracking().Where(drug => drug.IsActive).ToListAsync(cancellationToken);
        var drugIds = drugs.Select(drug => drug.Id).ToArray();
        var batches = await context.Batches.AsNoTracking()
            .Where(batch => drugIds.Contains(batch.DrugId))
            .ToListAsync(cancellationToken);
        var batchIds = batches.Select(batch => batch.Id).ToArray();
        var stock = await context.StockMovements.AsNoTracking()
            .Where(movement => batchIds.Contains(movement.BatchId))
            .GroupBy(movement => movement.BatchId)
            .Select(group => new { BatchId = group.Key, Quantity = group.Sum(movement => movement.QuantityChange) })
            .ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
        var choices = from batch in batches
                      join drug in drugs on batch.DrugId equals drug.Id
                      let available = stock.GetValueOrDefault(batch.Id)
                      where available > 0 &&
                            (!batch.ExpiryDate.HasValue || batch.ExpiryDate.Value >= DateOnly.FromDateTime(DateTime.Today))
                      orderby batch.ExpiryDate ?? DateOnly.MaxValue, drug.Name
                      select new WholesaleStockChoice(
                          drug.Id,
                          batch.Id,
                          drug.Name,
                          batch.BatchNo,
                          batch.ExpiryDate,
                          available,
                          batch.Mrp ?? drug.Mrp ?? 0m,
                          drug.Schedule,
                          drug.GstRate ?? 0m);
        StockChoices.Clear();
        foreach (var choice in choices)
        {
            StockChoices.Add(choice);
        }

        var series = scope.ServiceProvider.GetRequiredService<NumberSeriesService>();
        var profile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
        InvoiceNoPreview = await series.PreviewNextAsync(profile.InvoicePrefix, cancellationToken: cancellationToken);
    }

    [RelayCommand]
    private void AddLine()
    {
        Items.Add(new WholesaleInvoiceLineDraft
        {
            StockChoice = StockChoices.FirstOrDefault(),
            Quantity = 1m,
            UnitPrice = StockChoices.FirstOrDefault()?.Mrp ?? 0m
        });
        NotifyTotalsChanged();
    }

    [RelayCommand]
    private void RemoveLine(WholesaleInvoiceLineDraft? line)
    {
        if (line is not null)
        {
            Items.Remove(line);
            NotifyTotalsChanged();
        }
    }

    [RelayCommand]
    private async Task SaveInvoiceAsync()
    {
        ErrorMessage = string.Empty;
        var user = _session.User ?? throw new UnauthorizedAccessException("Sign in before creating invoices.");
        if (SelectedCustomer is null || Items.Count == 0 || Items.Any(item => item.StockChoice is null))
        {
            ErrorMessage = "Choose an active customer and at least one stocked item.";
            return;
        }

        var warningLines = Items.Where(item => item.StockChoice?.ExpiryDate is { } expiry &&
            expiry.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber <= 90).ToArray();
        if (warningLines.Length > 0 && !ConfirmNearExpiry)
        {
            if (!_confirmation.Confirm(
                    $"The invoice includes near-expiry batches: {string.Join(", ", warningLines.Select(line => line.StockChoice!.BatchNo))}. Confirm?",
                    "Near-expiry stock"))
            {
                return;
            }

            ConfirmNearExpiry = true;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<WholesaleInvoiceService>();
            var result = await service.SaveAsync(new SaveWholesaleInvoiceInput(
                SelectedCustomer.Id,
                Items.Select(item => new WholesaleInvoiceLineInput(
                    item.StockChoice!.DrugId,
                    item.StockChoice.BatchId,
                    item.Quantity,
                    item.FreeQuantity,
                    item.UnitPrice,
                    item.DiscountAmount)).ToArray(),
                PaidAmount,
                PaymentMethod,
                PaymentReference,
                ConfirmNearExpiry,
                90,
                OverrideCreditOrOverdue,
                OverrideReason,
                TransportDetails,
                VehicleNumber,
                EWayBillNumber,
                Irn,
                Notes),
                user.Id,
                user.Role);
            LastInvoiceId = result.Invoice.Id;
            StatusMessage = $"Invoice {result.Invoice.InvoiceNo} saved. Total {MoneyFormat.Rupees(result.Invoice.TotalAmount)}; outstanding {MoneyFormat.Rupees(result.OutstandingAfterPosting)}.";
            ErrorMessage = string.Empty;
            Items.Clear();
            await LoadAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorMessage = exception.Message;
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
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(EstimatedTax));
        OnPropertyChanged(nameof(EstimatedTotal));
    }
}
