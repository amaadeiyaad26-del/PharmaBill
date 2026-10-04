using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed partial class RecentBillsViewModel(
    IServiceScopeFactory scopeFactory,
    RetailBillPdfService pdfService,
    CurrentSession currentSession,
    IConfirmationService confirmationService) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _returnReason = string.Empty;

    [ObservableProperty]
    private bool _restockReturnedItems;

    [ObservableProperty]
    private RetailDocumentType _documentType = RetailDocumentType.CashMemo;

    [ObservableProperty]
    private string _emailRecipient = string.Empty;

    [ObservableProperty]
    private RecentRetailBill? _selectedBill;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public ObservableCollection<RecentRetailBill> Bills { get; } = [];

    public ObservableCollection<RetailSaleReturnableLine> ReturnableLines { get; } = [];

    public IReadOnlyList<RetailDocumentType> DocumentTypes { get; } = Enum.GetValues<RetailDocumentType>();

    partial void OnSelectedBillChanged(RecentRetailBill? value) =>
        _ = LoadReturnableLinesAsync();

    public async Task LoadReturnableLinesAsync(CancellationToken cancellationToken = default)
    {
        ReturnableLines.Clear();
        if (SelectedBill is null)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
            var lines = await service.GetSaleReturnLinesAsync(SelectedBill.SaleId, cancellationToken);
            foreach (var line in lines)
            {
                ReturnableLines.Add(line);
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
            var bills = await service.SearchRecentBillsAsync(SearchText, cancellationToken);
            Bills.Clear();
            foreach (var bill in bills)
            {
                Bills.Add(bill);
            }

            StatusMessage = $"{Bills.Count} bill(s) found.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private Task SearchAsync() => LoadAsync();

    public async Task ExportPdfAsync(string path, CancellationToken cancellationToken = default)
    {
        if (SelectedBill is null)
        {
            throw new InvalidOperationException("Select a bill first.");
        }

        await pdfService.ExportAsync(SelectedBill.SaleId, path, DocumentType, cancellationToken);
    }

    public async Task PreviewSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedBill is null)
        {
            throw new InvalidOperationException("Select a bill first.");
        }

        await pdfService.PreviewAsync(SelectedBill.SaleId, DocumentType, cancellationToken);
    }

    public async Task PrintSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedBill is null)
        {
            throw new InvalidOperationException("Select a bill first.");
        }

        await pdfService.PrintAsync(SelectedBill.SaleId, DocumentType, cancellationToken);
    }

    public async Task ShareSelectedToWhatsAppAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedBill is null)
        {
            throw new InvalidOperationException("Select a bill first.");
        }

        await pdfService.ShareWhatsAppAsync(SelectedBill.SaleId, DocumentType, cancellationToken);
    }

    public async Task EmailSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedBill is null)
        {
            throw new InvalidOperationException("Select a bill first.");
        }

        await pdfService.EmailAsync(SelectedBill.SaleId, DocumentType, EmailRecipient);
    }

    [RelayCommand]
    private async Task IssueSalesReturnAsync()
    {
        if (SelectedBill is null || currentSession.User is null ||
            string.IsNullOrWhiteSpace(ReturnReason))
        {
            ErrorMessage = "Select a bill, enter return quantities, and provide a reason.";
            return;
        }

        var selectedLines = ReturnableLines
            .Where(line => line.ReturnQuantity > 0)
            .Select(line => new RetailSaleReturnLineInput(line.SaleItemId, line.ReturnQuantity))
            .ToArray();
        if (selectedLines.Length == 0)
        {
            ErrorMessage = "Enter a return quantity for at least one bill item.";
            return;
        }

        if (!confirmationService.Confirm(
                $"Issue a credit note for the selected item quantities from {SelectedBill.InvoiceNo}?",
                "Confirm sales return"))
        {
            return;
        }

        if (RestockReturnedItems &&
            !confirmationService.Confirm(
                "Confirm that every returned pack is sealed, uncompromised, physically inspected, and not expired. Only then will its quantity be added back to saleable stock.",
                "Confirm returned stock is saleable"))
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
            var result = await service.IssueSalesReturnAsync(
                SelectedBill.SaleId,
                selectedLines,
                ReturnReason,
                RestockReturnedItems,
                currentSession.User.Id,
                currentSession.User.Role);
            ReturnReason = string.Empty;
            RestockReturnedItems = false;
            StatusMessage = $"Credit note {result.ReturnNo} issued for {MoneyFormat.Rupees(result.CreditAmount)}; " +
                            $"{result.RestockedQuantity:N2} unit(s) returned to stock.";
            ErrorMessage = string.Empty;
            await LoadReturnableLinesAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private Task IssueSalesReturnCommandAsync() => IssueSalesReturnAsync();
}
