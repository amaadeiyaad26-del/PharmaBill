using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed record WholesaleReturnInvoiceChoice(Guid Id, string InvoiceNo, string CustomerName, DateTime InvoiceAtUtc)
{
    public string Label => $"{InvoiceNo} — {CustomerName}";
}

public partial class WholesaleCreditLineDraft : ObservableObject
{
    public Guid InvoiceItemId { get; init; }
    public Guid BatchId { get; init; }
    public Guid DrugId { get; init; }
    public string DrugName { get; init; } = string.Empty;
    public string BatchNo { get; init; } = string.Empty;
    public decimal Remaining { get; init; }
    public decimal UnitCredit { get; init; }
    [ObservableProperty] private decimal _quantity;
    [ObservableProperty] private bool _restock;
    public bool Quarantine => !Restock;
}

public partial class WholesaleReturnsPageViewModel : ObservableObject, ILoadablePage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CurrentSession _session;

    public WholesaleReturnsPageViewModel(IServiceScopeFactory scopeFactory, CurrentSession session)
    {
        _scopeFactory = scopeFactory;
        _session = session;
    }

    public ObservableCollection<WholesaleReturnInvoiceChoice> Invoices { get; } = [];
    public ObservableCollection<WholesaleCreditLineDraft> InvoiceLines { get; } = [];
    public ObservableCollection<ExpiryReturnTrackerRow> ExpiryTracker { get; } = [];

    [ObservableProperty] private WholesaleReturnInvoiceChoice? _selectedInvoice;
    [ObservableProperty] private string _documentNo = string.Empty;
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string _debitAmount = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var invoices = await (from invoice in context.WholesaleInvoices.AsNoTracking()
                              join customer in context.Customers.AsNoTracking()
                                  on invoice.CustomerId equals customer.Id
                              where invoice.Status == "Posted"
                              orderby invoice.InvoiceAtUtc descending
                              select new WholesaleReturnInvoiceChoice(
                                  invoice.Id, invoice.InvoiceNo, customer.Name, invoice.InvoiceAtUtc))
            .ToListAsync(cancellationToken);
        Invoices.Clear();
        foreach (var invoice in invoices)
        {
            Invoices.Add(invoice);
        }

        var returns = scope.ServiceProvider.GetRequiredService<WholesaleReturnsService>();
        ExpiryTracker.Clear();
        foreach (var row in await returns.GetExpiryReturnTrackerAsync(
                     DateOnly.FromDateTime(DateTime.Today.AddDays(90)), cancellationToken))
        {
            ExpiryTracker.Add(row);
        }
    }

    partial void OnSelectedInvoiceChanged(WholesaleReturnInvoiceChoice? value)
    {
        if (value is not null)
        {
            _ = LoadInvoiceLinesAsync(value.Id);
        }
    }

    [RelayCommand]
    private async Task CreateCreditNoteAsync()
    {
        if (SelectedInvoice is null || string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Select a posted invoice and enter a return reason.";
            return;
        }

        var lines = InvoiceLines.Where(item => item.Quantity > 0)
            .Select(item => new WholesaleCreditLineInput(item.InvoiceItemId, item.Quantity, item.Restock))
            .ToArray();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var note = await scope.ServiceProvider.GetRequiredService<WholesaleReturnsService>()
                .CreateCreditNoteAsync(
                    SelectedInvoice.Id,
                    DocumentNo,
                    DateOnly.FromDateTime(DateTime.Today),
                    lines,
                    Reason,
                    _session.User?.Id ?? throw new UnauthorizedAccessException("Sign in before creating credit notes."),
                    _session.User.Role);
            StatusMessage = $"Credit note {note.ReturnNo} saved for {MoneyFormat.Rupees(note.TotalAmount)}. Unrestocked items are quarantined.";
            ErrorMessage = string.Empty;
            await LoadInvoiceLinesAsync(SelectedInvoice.Id);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task CreateDebitNoteAsync()
    {
        if (SelectedInvoice is null ||
            !decimal.TryParse(DebitAmount, out var amount) ||
            string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Select an invoice/customer, enter a positive amount and a reason.";
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            var invoice = await context.WholesaleInvoices.SingleAsync(item => item.Id == SelectedInvoice.Id);
            var note = await scope.ServiceProvider.GetRequiredService<WholesaleReturnsService>()
                .CreateDebitNoteAsync(
                    invoice.CustomerId,
                    DocumentNo,
                    DateOnly.FromDateTime(DateTime.Today),
                    amount,
                    Reason,
                    _session.User?.Id ?? throw new UnauthorizedAccessException("Sign in before creating debit notes."),
                    _session.User.Role);
            StatusMessage = $"Debit note {note.ReturnNo} saved.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorMessage = exception.Message;
        }
    }

    private async Task LoadInvoiceLinesAsync(Guid invoiceId)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var invoiceItems = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
                                  join drug in context.Drugs.AsNoTracking() on item.DrugId equals drug.Id
                                  join batch in context.Batches.AsNoTracking() on item.BatchId equals batch.Id
                                  where item.WholesaleInvoiceId == invoiceId
                                  select new
                                  {
                                      item.Id,
                                      item.BatchId,
                                      item.DrugId,
                                      drug.Name,
                                      batch.BatchNo,
                                      item.Quantity,
                                      item.LineTotal
                                  }).ToListAsync();
        var previouslyReturned = await context.WholesaleReturnItems.AsNoTracking()
            .Where(item => invoiceItems.Select(line => line.Id).Contains(item.WholesaleInvoiceItemId))
            .GroupBy(item => item.WholesaleInvoiceItemId)
            .Select(group => new { Id = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToDictionaryAsync(item => item.Id, item => item.Quantity);
        InvoiceLines.Clear();
        foreach (var line in invoiceItems)
        {
            var remaining = line.Quantity - previouslyReturned.GetValueOrDefault(line.Id);
            if (remaining <= 0)
            {
                continue;
            }

            InvoiceLines.Add(new WholesaleCreditLineDraft
            {
                InvoiceItemId = line.Id,
                BatchId = line.BatchId,
                DrugId = line.DrugId,
                DrugName = line.Name,
                BatchNo = line.BatchNo,
                Remaining = remaining,
                UnitCredit = line.Quantity == 0 ? 0m : line.LineTotal / line.Quantity
            });
        }
    }
}
