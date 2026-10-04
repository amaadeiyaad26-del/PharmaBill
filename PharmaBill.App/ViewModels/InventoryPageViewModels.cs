using System.Collections.ObjectModel;
using System.ComponentModel;
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

public interface ILoadablePage
{
    Task LoadAsync(CancellationToken cancellationToken = default);
}

public sealed record StockRowViewModel(
    Guid DrugId,
    string Medicine,
    string? Schedule,
    StockBatchRow Batch,
    string Status)
{
    public Guid BatchId => Batch.BatchId;
    public string BatchNo => Batch.BatchNo;
    public DateOnly? ExpiryDate => Batch.ExpiryDate;
    public decimal Mrp => Batch.Mrp;
    public decimal PurchaseRate => Batch.PurchaseRate;
    public decimal Quantity => Batch.Quantity;
}

public sealed partial class StockPageViewModel(
    IServiceScopeFactory scopeFactory,
    CurrentSession currentSession,
    IConfirmationService confirmationService,
    IPromptService promptService) :
    ObservableObject, ILoadablePage
{
    public const string SelectBatchMessage = "Select a batch in the grid first";

    [ObservableProperty]
    private string _nameFilter = string.Empty;

    [ObservableProperty]
    private string _batchFilter = string.Empty;

    [ObservableProperty]
    private string? _scheduleFilter = "All";

    [ObservableProperty]
    private string? _companyFilter;

    [ObservableProperty]
    private string? _statusFilter = "All";

    [ObservableProperty]
    private Guid? _supplierFilter;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StockBatchRow? _selectedBatch;

    [ObservableProperty]
    private StockRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _actionQuantityText = string.Empty;

    [ObservableProperty]
    private string _actionQuantityError = string.Empty;

    [ObservableProperty]
    private string _actionReason = string.Empty;

    [ObservableProperty]
    private string _actionReasonError = string.Empty;

    [ObservableProperty]
    private string _verificationSessionNo = DefaultSessionName();

    [ObservableProperty]
    private Guid? _activeVerificationSessionId;

    public ObservableCollection<StockDrugRow> Items { get; } = [];
    public ObservableCollection<StockRowViewModel> Rows { get; } = [];
    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<StockVerificationLineViewModel> VerificationItems { get; } = [];
    public string[] StatusOptions { get; } = ["All", "In stock", "Low stock", "Near expiry", "Expired"];
    public string[] ScheduleOptions { get; } = ["All", "OTC", "G", "H", "H1", "X", "NDPS"];
    public string[] ReasonOptions { get; } = ["Damage", "Theft", "Counting error", "Expired", "Other"];

    public static string DefaultSessionName() => $"Count {DateTime.Now:dd-MMM-yyyy HH:mm}";

    partial void OnSelectedRowChanged(StockRowViewModel? value) => SelectedBatch = value?.Batch;

    partial void OnNameFilterChanged(string value) => ApplyFilters();

    partial void OnBatchFilterChanged(string value) => ApplyFilters();

    partial void OnStatusFilterChanged(string? value) => ApplyFilters();

    partial void OnScheduleFilterChanged(string? value) => _ = LoadAsync();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            Suppliers.Clear();
            foreach (var supplier in await context.Suppliers
                         .Where(item => item.IsActive)
                         .OrderBy(item => item.Name)
                         .ToListAsync(cancellationToken))
            {
                Suppliers.Add(supplier);
            }

            var result = await inventory.GetStockAsync(
                ScheduleFilter is null or "All" ? null : ScheduleFilter,
                CompanyFilter,
                SupplierFilter,
                null,
                cancellationToken: cancellationToken);
            Items.Clear();
            foreach (var item in result)
            {
                Items.Add(item);
            }

            ApplyFilters();
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    // Builds one grid row per batch from the loaded stock, applying the name, batch and status filters.
    public void ApplyFilters()
    {
        var selectedId = SelectedRow?.BatchId;
        var name = NameFilter.Trim();
        var batchNo = BatchFilter.Trim();
        var rows = new List<StockRowViewModel>();
        foreach (var drug in Items)
        {
            if (name.Length > 0 && !drug.DrugName.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var batch in drug.Batches)
            {
                if (batch.Quantity <= 0 ||
                    (batchNo.Length > 0 && !batch.BatchNo.Contains(batchNo, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var status = RowStatus(drug, batch);
                var matches = StatusFilter switch
                {
                    "In stock" => true,
                    "Low stock" => drug.IsBelowReorderLevel,
                    "Near expiry" => batch.ExpiryStatus == "Near expiry",
                    "Expired" => batch.ExpiryStatus == "Expired",
                    _ => true
                };
                if (matches)
                {
                    rows.Add(new StockRowViewModel(drug.DrugId, drug.DrugName, drug.Schedule, batch, status));
                }
            }
        }

        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }

        SelectedRow = Rows.FirstOrDefault(row => row.BatchId == selectedId);
    }

    public static string RowStatus(StockDrugRow drug, StockBatchRow batch) =>
        batch.ExpiryStatus switch
        {
            "Expired" => "Expired",
            "Near expiry" => "Near expiry",
            _ => drug.IsBelowReorderLevel ? "Low stock" : "OK"
        };

    // Opens Stock filtered to a medicine, used by the search block's Adjust and Batches buttons.
    public async Task ShowMedicineAsync(string medicineName)
    {
        StatusFilter = "All";
        NameFilter = medicineName;
        await LoadAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private async Task AdjustStockAsync()
    {
        StatusMessage = string.Empty;
        ActionQuantityError = string.Empty;
        ActionReasonError = string.Empty;
        if (!TryGetUser(out var user))
        {
            return;
        }

        if (SelectedRow is null)
        {
            ErrorMessage = SelectBatchMessage;
            return;
        }

        var row = SelectedRow;
        if (!decimal.TryParse(ActionQuantityText.Trim(), NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var change) || change == 0)
        {
            ActionQuantityError = "Enter a quantity change other than 0 (use - to reduce)";
            ErrorMessage = string.Empty;
            return;
        }

        if (row.Quantity + change < 0)
        {
            ActionQuantityError = $"Stock cannot go below 0 (current stock {row.Quantity:0.##})";
            ErrorMessage = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(ActionReason))
        {
            ActionReasonError = "Reason is required";
            ErrorMessage = string.Empty;
            return;
        }

        if (!confirmationService.Confirm(
                $"Apply stock adjustment of {change:+0.##;-0.##} to {row.Medicine}, batch {row.BatchNo}?",
                "Confirm stock adjustment"))
        {
            return;
        }

        var reason = ActionReason.Trim();
        await RunInventoryActionAsync(async scope =>
        {
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryService>();
            await inventory.AdjustStockAsync(row.BatchId, change, reason, user.Id, user.Role);
        });
        if (ErrorMessage.Length == 0)
        {
            StatusMessage = $"Stock adjusted for {row.Medicine}, batch {row.BatchNo}.";
        }
    }

    [RelayCommand]
    private async Task WriteOffExpiredAsync()
    {
        StatusMessage = string.Empty;
        if (!TryGetUser(out var user))
        {
            return;
        }

        try
        {
            IReadOnlyList<ExpiredBatchRow> expired;
            using (var scope = scopeFactory.CreateScope())
            {
                expired = await scope.ServiceProvider.GetRequiredService<InventoryService>().GetExpiredBatchesAsync();
            }

            if (expired.Count == 0)
            {
                ErrorMessage = string.Empty;
                StatusMessage = "No expired batches with stock to write off.";
                return;
            }

            var list = string.Join(
                Environment.NewLine,
                expired.Take(15).Select(row =>
                    $"{row.DrugName} | batch {row.BatchNo} | exp {row.ExpiryDate:MM/yyyy} | qty {row.Quantity:0.##}"));
            if (expired.Count > 15)
            {
                list += $"{Environment.NewLine}...and {expired.Count - 15} more";
            }

            if (!confirmationService.Confirm(
                    $"Write off {expired.Count} expired batch(es)? A dump register entry is recorded for each.{Environment.NewLine}{Environment.NewLine}{list}",
                    "Write off expired stock"))
            {
                return;
            }

            var reason = string.IsNullOrWhiteSpace(ActionReason) ? "Expired" : ActionReason.Trim();
            int count;
            using (var scope = scopeFactory.CreateScope())
            {
                count = await scope.ServiceProvider.GetRequiredService<InventoryService>()
                    .WriteOffAllExpiredAsync(reason, user.Id, user.Role);
            }

            ErrorMessage = string.Empty;
            StatusMessage = $"{count} expired batch(es) written off.";
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task StartVerificationAsync()
    {
        StatusMessage = string.Empty;
        if (!TryGetUser(out var user) || string.IsNullOrWhiteSpace(VerificationSessionNo))
        {
            ErrorMessage = "Sign in and enter a count session name.";
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<StockVerificationService>();
            var session = await service.StartAsync(VerificationSessionNo.Trim(), user.Id, user.Role);
            ActiveVerificationSessionId = session.Id;
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            var items = await context.StockVerificationItems
                .Where(item => item.SessionId == session.Id)
                .Join(context.Batches, item => item.BatchId, batch => batch.Id,
                    (item, batch) => new { Item = item, Batch = batch })
                .Join(context.Drugs, row => row.Batch.DrugId, drug => drug.Id,
                    (row, drug) => new StockVerificationLineViewModel(
                        row.Item.BatchId,
                        $"{drug.Name} / {row.Batch.BatchNo}",
                        row.Item.ExpectedQuantity,
                        row.Item.CountedQuantity))
                .ToListAsync();
            VerificationItems.Clear();
            foreach (var item in items)
            {
                VerificationItems.Add(item);
            }

            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task PostVerificationAsync()
    {
        StatusMessage = string.Empty;
        if (!TryGetUser(out var user))
        {
            return;
        }

        if (!ActiveVerificationSessionId.HasValue)
        {
            ErrorMessage = "Start a physical count first.";
            return;
        }

        if (user.Role is not (UserRole.Owner or UserRole.Manager))
        {
            ErrorMessage = "Only the Owner or a Manager can post a physical count.";
            return;
        }

        var differences = VerificationItems.Where(item => item.Difference != 0).ToList();
        if (differences.Count == 0)
        {
            ErrorMessage = "No differences found. Enter the counted quantities first.";
            return;
        }

        var summary = string.Join(
            Environment.NewLine,
            differences.Take(15).Select(item =>
                $"{item.Display}: expected {item.ExpectedQuantity:0.##}, counted {item.CountedQuantity:0.##}, difference {item.Difference:+0.##;-0.##}"));
        if (!confirmationService.Confirm(
                $"Post these {differences.Count} difference(s)?{Environment.NewLine}{Environment.NewLine}{summary}",
                "Post physical count"))
        {
            return;
        }

        var reason = promptService.AskText(
            "Reason for stock differences", "Enter the reason for these adjustments.", "Reason");
        if (reason is null)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<StockVerificationService>();
            foreach (var item in VerificationItems)
            {
                await service.SetCountedQuantityAsync(
                    ActiveVerificationSessionId.Value,
                    item.BatchId,
                    item.CountedQuantity);
            }

            await service.PostAsync(ActiveVerificationSessionId.Value, user.Id, user.Role, reason);
            ActiveVerificationSessionId = null;
            VerificationItems.Clear();
            VerificationSessionNo = DefaultSessionName();
            ErrorMessage = string.Empty;
            StatusMessage = "Physical count posted.";
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private bool TryGetUser(out AppUser user)
    {
        user = currentSession.User!;
        if (user is not null)
        {
            return true;
        }

        ErrorMessage = "Sign in again to manage stock.";
        return false;
    }

    private async Task RunInventoryActionAsync(Func<IServiceScope, Task> action)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await action(scope);
            ActionQuantityText = string.Empty;
            ActionReason = string.Empty;
            ErrorMessage = string.Empty;
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }
}
public sealed partial class StockVerificationLineViewModel(
    Guid batchId,
    string display,
    decimal expectedQuantity,
    decimal countedQuantity) : ObservableObject
{
    public Guid BatchId { get; } = batchId;
    public string Display { get; } = display;
    public decimal ExpectedQuantity { get; } = expectedQuantity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Difference))]
    private decimal _countedQuantity = countedQuantity;

    public decimal Difference => CountedQuantity - ExpectedQuantity;
}

public sealed partial class PurchasePageViewModel(
    IServiceScopeFactory scopeFactory,
    CurrentSession currentSession,
    IFilePickerService filePicker,
    IConfirmationService confirmationService,
    PurchaseSpreadsheetReader spreadsheetReader,
    IPromptService promptService) : ObservableObject, ILoadablePage
{
    [ObservableProperty]
    private Supplier? _selectedSupplier;

    [ObservableProperty]
    private string _invoiceNo = string.Empty;

    [ObservableProperty]
    private DateTime _invoiceDate = DateTime.Today;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private BatchReturnOption? _selectedReturnBatch;

    [ObservableProperty]
    private string _returnNo = string.Empty;

    [ObservableProperty]
    private decimal _returnQuantity;

    [ObservableProperty]
    private string _returnReason = string.Empty;

    [ObservableProperty]
    private string _remarks = string.Empty;

    [ObservableProperty]
    private string _supplierError = string.Empty;

    [ObservableProperty]
    private string _invoiceNoError = string.Empty;

    [ObservableProperty]
    private string _invoiceDateError = string.Empty;

    [ObservableProperty]
    private bool _hasSpreadsheet;

    [ObservableProperty]
    private System.Data.DataView? _spreadsheetPreview;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string _importStatus = string.Empty;

    [ObservableProperty]
    private string _billTotalCheck = string.Empty;

    [ObservableProperty]
    private bool _billTotalMatches;

    [ObservableProperty]
    private bool _isAddItemOpen;

    [ObservableProperty]
    private Drug? _newItemDrug;

    [ObservableProperty]
    private string _newItemBatch = string.Empty;

    [ObservableProperty]
    private string _newItemExpiry = string.Empty;

    [ObservableProperty]
    private string _newItemQuantity = string.Empty;

    [ObservableProperty]
    private string _newItemFree = string.Empty;

    [ObservableProperty]
    private string _newItemMrp = string.Empty;

    [ObservableProperty]
    private string _newItemRate = string.Empty;

    [ObservableProperty]
    private string _newItemGst = string.Empty;

    [ObservableProperty]
    private string _newItemRack = string.Empty;

    [ObservableProperty]
    private string _newItemError = string.Empty;

    private decimal? _importedExpectedGrandTotal;

    public ObservableCollection<Supplier> Suppliers { get; } = [];
    public ObservableCollection<Drug> Drugs { get; } = [];
    public ObservableCollection<BatchReturnOption> ReturnBatches { get; } = [];
    public ObservableCollection<PurchaseReturnLineDraft> ReturnLines { get; } = [];
    public ObservableCollection<PurchaseLineDraft> Lines { get; } = [];
    public ObservableCollection<string> SpreadsheetHeaders { get; } = [string.Empty];
    public ObservableCollection<SpreadsheetColumnMapping> SpreadsheetMappings { get; } =
    [
        new("Medicine", true, []),
        new("Batch", true, []),
        new("Expiry", true, []),
        new("Quantity", true, []),
        new("Free quantity", false, []),
        new("MRP", true, []),
        new("Rate", true, []),
        new("GST", false, []),
        new("Discount", false, []),
        new("Amount", true, [])
    ];
    private PurchaseSpreadsheetData? _spreadsheet;
    public decimal Subtotal => Lines.Sum(line => line.BaseAmount);
    public decimal TaxTotal => Lines.Sum(line => line.TaxAmount);
    public decimal GrandTotal => Subtotal + TaxTotal;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        Suppliers.Clear();
        foreach (var supplier in await context.Suppliers
                     .Where(item => item.IsActive)
                     .OrderBy(item => item.Name)
                     .ToListAsync(cancellationToken))
        {
            Suppliers.Add(supplier);
        }

        Drugs.Clear();
        foreach (var drug in await context.Drugs
                     .Where(item => item.IsActive)
                     .OrderBy(item => item.Name)
                     .ToListAsync(cancellationToken))
        {
            Drugs.Add(drug);
        }

        var supplierNames = Suppliers.ToDictionary(supplier => supplier.Id, supplier => supplier.Name);
        var drugNames = Drugs.ToDictionary(drug => drug.Id, drug => drug.Name);
        ReturnBatches.Clear();
        foreach (var batch in await context.Batches
                     .Where(item => item.SupplierId.HasValue && item.Quantity > 0)
                     .OrderBy(item => item.BatchNo)
                     .ToListAsync(cancellationToken))
        {
            if (batch.SupplierId is { } supplierId &&
                supplierNames.TryGetValue(supplierId, out var supplierName) &&
                drugNames.TryGetValue(batch.DrugId, out var drugName))
            {
                ReturnBatches.Add(new BatchReturnOption(
                    batch.Id,
                    supplierId,
                    batch.BatchNo,
                    $"{drugName} / {batch.BatchNo} / {supplierName} / Qty {batch.Quantity}"));
            }
        }
    }

    [RelayCommand]
    private async Task AddSupplierAsync()
    {
        if (currentSession.User is null)
        {
            ErrorMessage = "Sign in again to add a supplier.";
            return;
        }

        var name = promptService.AskText("Add new supplier", "Enter the supplier name.", "Supplier name");
        if (name is null)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var supplier = await scope.ServiceProvider.GetRequiredService<AddStockService>()
                .AddSupplierAsync(name, currentSession.User.Id, currentSession.User.Role);
            var existing = Suppliers.FirstOrDefault(item => item.Id == supplier.Id);
            if (existing is null)
            {
                Suppliers.Add(supplier);
                existing = supplier;
            }

            SelectedSupplier = existing;
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private void AddLine()
    {
        NewItemError = string.Empty;
        IsAddItemOpen = true;
    }

    [RelayCommand]
    private void CancelAddItem()
    {
        IsAddItemOpen = false;
        ClearNewItem();
    }

    [RelayCommand]
    private void ConfirmAddItem()
    {
        NewItemError = string.Empty;
        if (NewItemDrug is null)
        {
            NewItemError = "Medicine is required. To add a medicine that is not listed, use the search box above.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewItemBatch))
        {
            NewItemError = "Batch no. is required";
            return;
        }

        if (!AddStockViewModel.TryParseExpiry(NewItemExpiry, out var expiry))
        {
            NewItemError = "Expiry (month/year) is required, for example 08/2027";
            return;
        }

        if (expiry < DateOnly.FromDateTime(DateTime.Today))
        {
            NewItemError = "This date has expired; expired stock cannot be purchased";
            return;
        }

        if (!TryDecimal(NewItemQuantity, out var quantity) || quantity <= 0)
        {
            NewItemError = "Quantity must be more than 0";
            return;
        }

        if (!TryDecimal(NewItemMrp, out var mrp) || mrp < 0 || !TryDecimal(NewItemRate, out var rate) || rate < 0)
        {
            NewItemError = "MRP and Rate are required numbers";
            return;
        }

        var free = OptionalDecimal(NewItemFree);
        var gst = OptionalDecimal(NewItemGst);
        if (free < 0 || gst < 0 || gst > 100)
        {
            NewItemError = "Free quantity and GST % must be valid numbers";
            return;
        }

        var line = new PurchaseLineDraft(NewItemDrug)
        {
            BatchNo = NewItemBatch.Trim(),
            Expiry = expiry.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Quantity = quantity,
            FreeQuantity = free,
            Mrp = mrp,
            Rate = rate,
            GstRate = gst,
            Rack = string.IsNullOrWhiteSpace(NewItemRack) ? null : NewItemRack.Trim()
        };
        AddDraftLine(line);
        ClearNewItem();
        IsAddItemOpen = false;
    }

    [RelayCommand]
    private void DeleteLine(PurchaseLineDraft? line)
    {
        if (line is null)
        {
            return;
        }

        line.PropertyChanged -= OnLineChanged;
        Lines.Remove(line);
        NotifyTotals();
    }

    [RelayCommand]
    private void CancelSpreadsheet()
    {
        _spreadsheet = null;
        SpreadsheetPreview = null;
        HasSpreadsheet = false;
        StatusMessage = string.Empty;
    }

    private void AddDraftLine(PurchaseLineDraft line)
    {
        line.PropertyChanged += OnLineChanged;
        Lines.Add(line);
        NotifyTotals();
    }

    private void ClearNewItem()
    {
        NewItemDrug = null;
        NewItemBatch = string.Empty;
        NewItemExpiry = string.Empty;
        NewItemQuantity = string.Empty;
        NewItemFree = string.Empty;
        NewItemMrp = string.Empty;
        NewItemRate = string.Empty;
        NewItemGst = string.Empty;
        NewItemRack = string.Empty;
        NewItemError = string.Empty;
    }

    private void NotifyTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(TaxTotal));
        OnPropertyChanged(nameof(GrandTotal));
    }

    [RelayCommand]
    private async Task ImportDocumentAsync()
    {
        var path = filePicker.PickPurchaseDocument();
        if (path is null)
        {
            return;
        }

        await ImportDocumentFromPathAsync(path);
    }

    public async Task ImportDocumentFromPathAsync(string path)
    {
        if (!confirmationService.Confirm(
                "The selected supplier document will be sent to Google Gemini for extraction. Review the returned rows before saving.",
                "Send purchase document to Gemini"))
        {
            return;
        }

        IsImporting = true;
        ImportStatus = "Reading the bill...";
        BillTotalCheck = string.Empty;
        try
        {
            await ImportCoreAsync(path);
            ImportStatus = ErrorMessage.Length > 0 && Lines.Count == 0 && ReturnLines.Count == 0
                ? ErrorMessage
                : "Read by: Gemini";
        }
        finally
        {
            IsImporting = false;
        }
    }

    private async Task ImportCoreAsync(string path)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var extractor = scope.ServiceProvider.GetRequiredService<GeminiPurchaseImportService>();
            var extracted = await extractor.ExtractAsync(path);
            if (extracted.DocumentType == "PURCHASE_RETURN")
            {
                _importedExpectedGrandTotal = null;
                if (ReturnLines.Count > 0 &&
                    !confirmationService.Confirm("Replace the purchase-return lines currently staged?", "Replace return lines"))
                {
                    return;
                }

                SelectedSupplier = Suppliers.FirstOrDefault(supplier =>
                    string.Equals(supplier.Name.Trim(), extracted.Supplier.Trim(), StringComparison.OrdinalIgnoreCase));
                ReturnNo = extracted.InvoiceNo;
                ReturnLines.Clear();
                foreach (var extractedLine in extracted.Items)
                {
                    var batch = ReturnBatches.FirstOrDefault(option =>
                        option.SupplierId == SelectedSupplier?.Id &&
                        string.Equals(option.BatchNo, extractedLine.Batch, StringComparison.OrdinalIgnoreCase));
                    ReturnLines.Add(new PurchaseReturnLineDraft(batch, extractedLine.Quantity));
                }

                ErrorMessage = SelectedSupplier is null || ReturnLines.Any(line => line.Batch is null)
                    ? "Review the return rows, select the matching supplier and match every extracted batch to existing stock."
                    : string.Empty;
                StatusMessage = $"Extracted {ReturnLines.Count} return rows. Review each batch and quantity, enter a reason, then post.";
                return;
            }

            if (extracted.DocumentType != "PURCHASE_INVOICE")
            {
                ErrorMessage = "The document type is not supported.";
                return;
            }

            if (Lines.Count > 0 &&
                !confirmationService.Confirm("Replace the purchase lines currently in the review grid?", "Replace purchase lines"))
            {
                return;
            }

            Lines.Clear();
            SelectedSupplier = Suppliers.FirstOrDefault(supplier =>
                string.Equals(supplier.Name.Trim(), extracted.Supplier.Trim(), StringComparison.OrdinalIgnoreCase));
            InvoiceNo = extracted.InvoiceNo;
            if (DateOnly.TryParse(extracted.InvoiceDate, CultureInfo.InvariantCulture, out var parsedDate))
            {
                InvoiceDate = parsedDate.ToDateTime(TimeOnly.MinValue);
            }

            foreach (var extractedLine in extracted.Items)
            {
                var medicine = Drugs.FirstOrDefault(drug =>
                    string.Equals(drug.Name.Trim(), extractedLine.ItemName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(drug.BrandName?.Trim(), extractedLine.ItemName.Trim(), StringComparison.OrdinalIgnoreCase));
                var line = new PurchaseLineDraft(medicine)
                {
                    BatchNo = extractedLine.Batch,
                    Expiry = DateOnly.TryParse(
                        extractedLine.Expiry,
                        CultureInfo.InvariantCulture,
                        out var expiryDate)
                        ? expiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : extractedLine.Expiry,
                    Quantity = extractedLine.Quantity,
                    FreeQuantity = extractedLine.Free,
                    Mrp = extractedLine.Mrp,
                    Rate = extractedLine.Rate,
                    GstRate = extractedLine.Gst,
                    Rack = null
                };
                line.PropertyChanged += OnLineChanged;
                Lines.Add(line);
            }

            OnPropertyChanged(nameof(Subtotal));
            OnPropertyChanged(nameof(TaxTotal));
            OnPropertyChanged(nameof(GrandTotal));
            ErrorMessage = SelectedSupplier is null
                ? $"Review the extracted rows and select the matching supplier. Extracted supplier: {extracted.Supplier}."
                : string.Empty;
            StatusMessage = $"Extracted {Lines.Count} rows. Confirm medicine matches, expiry dates, and totals before saving.";
            if (decimal.Round(GrandTotal, 2, MidpointRounding.AwayFromZero) !=
                decimal.Round(extracted.GrandTotal, 2, MidpointRounding.AwayFromZero))
            {
                _importedExpectedGrandTotal = extracted.GrandTotal;
                BillTotalMatches = false;
                BillTotalCheck = $"Bill total {extracted.GrandTotal:F2} does not match the calculated total {GrandTotal:F2}. Check the rows.";
                ErrorMessage = $"Extracted bill total {extracted.GrandTotal:F2} differs from the calculated total {GrandTotal:F2}. Review and correct before saving.";
            }
            else
            {
                _importedExpectedGrandTotal = extracted.GrandTotal;
                BillTotalMatches = true;
                BillTotalCheck = "Bill totals match";
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task LoadSpreadsheetAsync()
    {
        var path = filePicker.PickPurchaseSpreadsheet();
        if (path is null)
        {
            return;
        }

        try
        {
            _spreadsheet = await spreadsheetReader.ReadAsync(path);
            SpreadsheetHeaders.Clear();
            SpreadsheetHeaders.Add(string.Empty);
            foreach (var header in _spreadsheet.Headers)
            {
                SpreadsheetHeaders.Add(header);
            }

            foreach (var mapping in SpreadsheetMappings)
            {
                mapping.AvailableHeaders = SpreadsheetHeaders;
                mapping.SourceHeader = FindLikelyHeader(mapping.Target, _spreadsheet.Headers);
            }

            StatusMessage = $"Loaded {_spreadsheet.Rows.Count} rows. Map the columns and apply them to the review grid.";
            ErrorMessage = string.Empty;
            SpreadsheetPreview = BuildPreview(_spreadsheet);
            HasSpreadsheet = true;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private void ApplySpreadsheetMapping()
    {
        if (_spreadsheet is null)
        {
            ErrorMessage = "Choose a CSV or XLSX file first.";
            return;
        }

        if (Lines.Count > 0 &&
            !confirmationService.Confirm("Replace the purchase lines currently in the review grid?", "Replace purchase lines"))
        {
            return;
        }

        var mappings = SpreadsheetMappings.ToDictionary(
            mapping => mapping.Target,
            mapping => mapping.SourceHeader,
            StringComparer.Ordinal);
        if (SpreadsheetMappings.Any(mapping =>
                mapping.IsRequired && string.IsNullOrWhiteSpace(mapping.SourceHeader)))
        {
            ErrorMessage = "Map every required purchase field before applying the spreadsheet.";
            return;
        }

        var mappedRows = new List<PurchaseLineDraft>();
        for (var rowIndex = 0; rowIndex < _spreadsheet.Rows.Count; rowIndex++)
        {
            var row = _spreadsheet.Rows[rowIndex];
            var itemName = Read(row, mappings, "Medicine");
            if (!TryDecimal(Read(row, mappings, "Quantity"), out var quantity) ||
                !TryDecimal(Read(row, mappings, "MRP"), out var mrp) ||
                !TryDecimal(Read(row, mappings, "Rate"), out var rate) ||
                !TryDecimal(Read(row, mappings, "Amount"), out var amount))
            {
                ErrorMessage = $"Row {rowIndex + 2} has an invalid quantity, MRP, rate or amount.";
                return;
            }

            var free = OptionalDecimal(Read(row, mappings, "Free quantity"));
            var gst = OptionalDecimal(Read(row, mappings, "GST"));
            var discount = OptionalDecimal(Read(row, mappings, "Discount"));
            if (free < 0 || gst < 0 || discount < 0)
            {
                ErrorMessage = $"Row {rowIndex + 2} has an invalid free quantity, GST or discount.";
                return;
            }

            var expectedAmount = decimal.Round(quantity * rate - discount, 2, MidpointRounding.AwayFromZero);
            if (expectedAmount != decimal.Round(amount, 2, MidpointRounding.AwayFromZero))
            {
                ErrorMessage = $"Row {rowIndex + 2}: quantity × rate less discount does not equal amount.";
                return;
            }

            var expiry = Read(row, mappings, "Expiry");
            var drug = Drugs.FirstOrDefault(item =>
                string.Equals(item.Name.Trim(), itemName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.BrandName?.Trim(), itemName.Trim(), StringComparison.OrdinalIgnoreCase));
            var line = new PurchaseLineDraft(drug)
            {
                BatchNo = Read(row, mappings, "Batch"),
                Expiry = DateOnly.TryParse(expiry, CultureInfo.CurrentCulture, DateTimeStyles.None, out var expiryDate)
                    ? expiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : expiry,
                Quantity = quantity,
                FreeQuantity = free,
                Mrp = mrp,
                Rate = rate,
                GstRate = gst,
                DiscountAmount = discount
            };
            line.PropertyChanged += OnLineChanged;
            mappedRows.Add(line);
        }

        Lines.Clear();
        _importedExpectedGrandTotal = null;
        foreach (var line in mappedRows)
        {
            Lines.Add(line);
        }

        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(TaxTotal));
        OnPropertyChanged(nameof(GrandTotal));
        ErrorMessage = string.Empty;
        StatusMessage = $"Mapped {Lines.Count} rows. Confirm each medicine and expiry before saving.";
        HasSpreadsheet = false;
        SpreadsheetPreview = null;
        _spreadsheet = null;
    }

    private static System.Data.DataView BuildPreview(PurchaseSpreadsheetData data)
    {
        var table = new System.Data.DataTable();
        var headers = data.Headers.Where(header => !string.IsNullOrWhiteSpace(header)).Distinct().ToList();
        foreach (var header in headers)
        {
            table.Columns.Add(header, typeof(string));
        }

        foreach (var row in data.Rows.Take(5))
        {
            table.Rows.Add(headers.Select(header => (object)row.GetValueOrDefault(header, string.Empty)).ToArray());
        }

        return table.DefaultView;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        SupplierError = SelectedSupplier is null ? "Supplier is required" : string.Empty;
        InvoiceNoError = string.IsNullOrWhiteSpace(InvoiceNo) ? "Supplier invoice no. is required" : string.Empty;
        InvoiceDateError = string.Empty;
        if (SupplierError.Length > 0 || InvoiceNoError.Length > 0)
        {
            return;
        }

        if (SelectedSupplier is null || currentSession.User is null)
        {
            ErrorMessage = "Select a supplier and sign in again.";
            return;
        }

        if (!DateOnly.TryParse(InvoiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), out var invoiceDate))
        {
            ErrorMessage = "Enter a valid invoice date.";
            return;
        }

        if (Lines.Count == 0 || Lines.Any(line => !line.TryCreateInput(out _)))
        {
            ErrorMessage = "Complete each purchase line with a medicine, batch, expiry, quantity and rate.";
            return;
        }

        if (_importedExpectedGrandTotal.HasValue &&
            decimal.Round(GrandTotal, 2, MidpointRounding.AwayFromZero) !=
            decimal.Round(_importedExpectedGrandTotal.Value, 2, MidpointRounding.AwayFromZero))
        {
            ErrorMessage = $"Calculated total {GrandTotal:F2} still does not match the imported bill total {_importedExpectedGrandTotal.Value:F2}.";
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<PurchaseService>();
            if (await service.IsDuplicateInvoiceAsync(SelectedSupplier.Id, InvoiceNo))
            {
                ErrorMessage = "This supplier already has an invoice with that number.";
                return;
            }

            var lines = Lines.Select(line =>
            {
                line.TryCreateInput(out var input);
                return input;
            }).ToArray();
            await service.SavePurchaseAsync(
                new SavePurchaseInput(
                    SelectedSupplier.Id,
                    InvoiceNo,
                    invoiceDate,
                    Subtotal,
                    0m,
                    TaxTotal,
                    GrandTotal,
                    lines,
                    Remarks),
                currentSession.User.Id,
                currentSession.User.Role);
            Lines.Clear();
            _importedExpectedGrandTotal = null;
            InvoiceNo = string.Empty;
            StatusMessage = "Purchase invoice saved.";
            OnPropertyChanged(nameof(Subtotal));
            OnPropertyChanged(nameof(TaxTotal));
            OnPropertyChanged(nameof(GrandTotal));
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task SavePurchaseReturnAsync()
    {
        if (SelectedSupplier is null || currentSession.User is null ||
            string.IsNullOrWhiteSpace(ReturnNo) || string.IsNullOrWhiteSpace(ReturnReason))
        {
            ErrorMessage = "Select a supplier, enter a return number and reason, and provide return lines.";
            return;
        }

        var returnLines = ReturnLines.Count > 0
            ? ReturnLines.Select(line => new PurchaseReturnLineInput(line.Batch?.BatchId ?? Guid.Empty, line.Quantity)).ToArray()
            : SelectedReturnBatch is not null
                ? [new PurchaseReturnLineInput(SelectedReturnBatch.BatchId, ReturnQuantity)]
                : [];
        if (returnLines.Length == 0 || returnLines.Any(line => line.BatchId == Guid.Empty || line.Quantity <= 0))
        {
            ErrorMessage = "Match every return line to an existing batch and enter positive quantities.";
            return;
        }

        if (returnLines.Any(line =>
                !ReturnBatches.Any(batch =>
                    batch.BatchId == line.BatchId && batch.SupplierId == SelectedSupplier.Id)))
        {
            ErrorMessage = "Every selected return batch must belong to the selected supplier.";
            return;
        }

        if (!confirmationService.Confirm(
                $"Post this purchase return for {returnLines.Sum(line => line.Quantity)} total units to {SelectedSupplier.Name}?",
                "Confirm purchase return"))
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<PurchaseService>();
            await service.SavePurchaseReturnAsync(
                SelectedSupplier.Id,
                ReturnNo,
                DateOnly.FromDateTime(DateTime.Today),
                returnLines,
                ReturnReason,
                currentSession.User.Id,
                currentSession.User.Role);
            ReturnNo = string.Empty;
            ReturnQuantity = 0;
            ReturnReason = string.Empty;
            ReturnLines.Clear();
            SelectedReturnBatch = null;
            StatusMessage = "Purchase return posted and stock reduced.";
            await LoadAsync();
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(TaxTotal));
        OnPropertyChanged(nameof(GrandTotal));
    }

    private static string FindLikelyHeader(string target, IReadOnlyList<string> headers)
    {
        var candidates = target switch
        {
            "Medicine" => new[] { "medicine", "drug", "item", "product" },
            "Batch" => new[] { "batch", "lot" },
            "Expiry" => new[] { "expiry", "expdate", "expiration" },
            "Quantity" => new[] { "quantity", "qty" },
            "Free quantity" => new[] { "freequantity", "freeqty", "free" },
            "MRP" => new[] { "mrp" },
            "Rate" => new[] { "rate", "ptr", "price" },
            "GST" => new[] { "gst", "tax" },
            "Discount" => new[] { "discount" },
            "Amount" => new[] { "amount", "linevalue", "total" },
            _ => []
        };
        return headers.FirstOrDefault(header =>
        {
            var normalizedHeader = header.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
            return candidates.Any(candidate =>
                normalizedHeader.Contains(candidate, StringComparison.OrdinalIgnoreCase));
        }) ?? string.Empty;
    }

    private static string Read(
        IReadOnlyDictionary<string, string> row,
        IReadOnlyDictionary<string, string> mappings,
        string target)
    {
        var header = mappings[target];
        return string.IsNullOrWhiteSpace(header) ? string.Empty : row.GetValueOrDefault(header, string.Empty);
    }

    private static decimal OptionalDecimal(string value) =>
        string.IsNullOrWhiteSpace(value) ? 0m : TryDecimal(value, out var parsed) ? parsed : -1m;

    private static bool TryDecimal(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
}

public sealed record BatchReturnOption(Guid BatchId, Guid SupplierId, string BatchNo, string Display);

public sealed partial class PurchaseReturnLineDraft(
    BatchReturnOption? batch,
    decimal quantity) : ObservableObject
{
    [ObservableProperty]
    private BatchReturnOption? _batch = batch;

    [ObservableProperty]
    private decimal _quantity = quantity;
}

public sealed partial class SpreadsheetColumnMapping(
    string target,
    bool isRequired,
    ObservableCollection<string> availableHeaders) : ObservableObject
{
    public string Target { get; } = target;
    public string Label => Target == "GST" ? "GST %" : Target;
    public bool IsRequired { get; } = isRequired;

    [ObservableProperty]
    private ObservableCollection<string> _availableHeaders = availableHeaders;

    [ObservableProperty]
    private string _sourceHeader = string.Empty;
}

public sealed partial class PurchaseLineDraft(Drug? drug) : ObservableObject
{
    [ObservableProperty]
    private Drug? _drug = drug;

    [ObservableProperty]
    private string _batchNo = string.Empty;

    [ObservableProperty]
    private string _expiry = string.Empty;

    [ObservableProperty]
    private decimal _quantity;

    [ObservableProperty]
    private decimal _freeQuantity;

    [ObservableProperty]
    private decimal _mrp;

    [ObservableProperty]
    private decimal _rate;

    [ObservableProperty]
    private decimal _gstRate;

    [ObservableProperty]
    private decimal _discountAmount;

    [ObservableProperty]
    private string? _rack;

    public decimal BaseAmount => Round(Quantity * Rate - DiscountAmount);
    public decimal TaxAmount => Round(BaseAmount * GstRate / 100m);
    public decimal Amount => BaseAmount;

    public bool TryCreateInput(out PurchaseLineInput input)
    {
        input = null!;
        if (Drug is null ||
            !DateOnly.TryParse(Expiry, CultureInfo.CurrentCulture, DateTimeStyles.None, out var expiryDate))
        {
            return false;
        }

        input = new PurchaseLineInput(
            Drug.Id,
            BatchNo,
            expiryDate,
            Quantity,
            FreeQuantity,
            Mrp,
            Rate,
            GstRate,
            DiscountAmount,
            Amount,
            Rack);
        return true;
    }

    partial void OnQuantityChanged(decimal value) => NotifyAmounts();
    partial void OnRateChanged(decimal value) => NotifyAmounts();
    partial void OnGstRateChanged(decimal value) => NotifyAmounts();
    partial void OnDiscountAmountChanged(decimal value) => NotifyAmounts();

    private void NotifyAmounts()
    {
        OnPropertyChanged(nameof(BaseAmount));
        OnPropertyChanged(nameof(TaxAmount));
        OnPropertyChanged(nameof(Amount));
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
