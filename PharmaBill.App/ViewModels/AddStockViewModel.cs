using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed record AddStockRequest(
    Guid? DrugId,
    Guid? CatalogMedicineId,
    string MedicineName,
    string? Composition,
    string? Manufacturer);

public sealed partial class AddStockViewModel : ObservableObject
{
    private static readonly string[] ExpiryFormats = ["MM/yyyy", "M/yyyy", "MM-yyyy", "M-yyyy", "MM/yy", "M/yy"];
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly CurrentSession? _session;
    private readonly IConfirmationService _confirmation;
    private readonly IPromptService? _prompt;

    public AddStockViewModel(
        IServiceScopeFactory? scopeFactory,
        CurrentSession? session,
        IConfirmationService confirmation,
        IPromptService? prompt)
    {
        _scopeFactory = scopeFactory;
        _session = session;
        _confirmation = confirmation;
        _prompt = prompt;
    }

    public IReadOnlyList<string> ScheduleOptions { get; } = AddStockService.Schedules;
    public ObservableCollection<Supplier> Suppliers { get; } = [];

    public Guid? DrugId { get; private set; }
    public Guid? CatalogMedicineId { get; private set; }

    [ObservableProperty]
    private bool _isManualMedicine;

    [ObservableProperty]
    private string _medicineName = string.Empty;

    [ObservableProperty]
    private string _composition = string.Empty;

    [ObservableProperty]
    private string _manufacturer = string.Empty;

    [ObservableProperty]
    private string? _schedule;

    [ObservableProperty]
    private string _scheduleHint = string.Empty;

    [ObservableProperty]
    private string _batchNo = string.Empty;

    [ObservableProperty]
    private string _expiryText = string.Empty;

    [ObservableProperty]
    private string _mrpText = string.Empty;

    [ObservableProperty]
    private string _purchaseRateText = string.Empty;

    [ObservableProperty]
    private string _quantityText = string.Empty;

    [ObservableProperty]
    private string _freeQuantityText = string.Empty;

    [ObservableProperty]
    private string _packSizeText = string.Empty;

    [ObservableProperty]
    private string _gstText = string.Empty;

    [ObservableProperty]
    private Supplier? _selectedSupplier;

    [ObservableProperty]
    private string _supplierInvoiceNo = string.Empty;

    [ObservableProperty]
    private DateTime? _invoiceDate;

    [ObservableProperty]
    private string _rack = string.Empty;

    [ObservableProperty]
    private string _medicineError = string.Empty;

    [ObservableProperty]
    private string _scheduleError = string.Empty;

    [ObservableProperty]
    private string _batchNoError = string.Empty;

    [ObservableProperty]
    private string _expiryError = string.Empty;

    [ObservableProperty]
    private string _mrpError = string.Empty;

    [ObservableProperty]
    private string _purchaseRateError = string.Empty;

    [ObservableProperty]
    private string _quantityError = string.Empty;

    [ObservableProperty]
    private string _freeQuantityError = string.Empty;

    [ObservableProperty]
    private string _packSizeError = string.Empty;

    [ObservableProperty]
    private string _gstError = string.Empty;

    [ObservableProperty]
    private string _supplierError = string.Empty;

    [ObservableProperty]
    private string _supplierInvoiceNoError = string.Empty;

    [ObservableProperty]
    private string _invoiceDateError = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isReadOnlyMode;

    [ObservableProperty]
    private string _firstInvalidField = string.Empty;

    public bool IsMedicineReadOnly => !IsManualMedicine;

    partial void OnIsManualMedicineChanged(bool value) => OnPropertyChanged(nameof(IsMedicineReadOnly));

    public string Title => IsManualMedicine ? "Add new medicine and stock" : "Add stock";

    public event EventHandler<AddStockResult>? Saved;

    public async Task InitializeAsync(AddStockRequest request)
    {
        DrugId = request.DrugId;
        CatalogMedicineId = request.CatalogMedicineId;
        IsManualMedicine = string.IsNullOrWhiteSpace(request.MedicineName);
        MedicineName = request.MedicineName;
        Composition = request.Composition ?? string.Empty;
        Manufacturer = request.Manufacturer ?? string.Empty;
        OnPropertyChanged(nameof(Title));
        if (_scopeFactory is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        foreach (var supplier in await context.Suppliers.Where(item => item.IsActive).OrderBy(item => item.Name).ToListAsync())
        {
            Suppliers.Add(supplier);
        }

        var entitlement = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        IsReadOnlyMode = await entitlement.IsReadOnlyAsync();
        if (IsReadOnlyMode)
        {
            ErrorMessage = "Read-only mode: stock entry is blocked until the trial or licence is renewed.";
        }

        if (!IsManualMedicine)
        {
            var service = scope.ServiceProvider.GetRequiredService<AddStockService>();
            var suggestion = await service.SuggestScheduleAsync(DrugId, CatalogMedicineId, MedicineName);
            ScheduleHint = suggestion is null
                ? "Choose the schedule from the pack label"
                : $"Suggested: {suggestion}. Please choose to confirm.";
        }
    }

    [RelayCommand]
    private async Task AddSupplierAsync()
    {
        if (_scopeFactory is null || _session?.User is null || _prompt is null)
        {
            return;
        }

        var name = _prompt.AskText("Add new supplier", "Enter the supplier name.", "Supplier name");
        if (name is null)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AddStockService>();
            var supplier = await service.AddSupplierAsync(name, _session.User.Id, _session.User.Role);
            var existing = Suppliers.FirstOrDefault(item => item.Id == supplier.Id);
            if (existing is null)
            {
                Suppliers.Add(supplier);
                existing = supplier;
            }

            SelectedSupplier = existing;
            SupplierError = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    // Field rules only; no dialogs, so it can be tested without a window.
    public bool Validate()
    {
        MedicineError = IsManualMedicine && string.IsNullOrWhiteSpace(MedicineName) ? "Medicine name is required" : string.Empty;
        ScheduleError = string.IsNullOrWhiteSpace(Schedule) ? "Choose the schedule" : string.Empty;
        BatchNoError = string.IsNullOrWhiteSpace(BatchNo) ? "Batch no. is required" : string.Empty;
        ExpiryError = string.Empty;
        if (string.IsNullOrWhiteSpace(ExpiryText))
        {
            ExpiryError = "Expiry (month/year) is required";
        }
        else if (!TryParseExpiry(ExpiryText, out var expiry))
        {
            ExpiryError = "Use month/year, for example 08/2027";
        }
        else if (expiry < DateOnly.FromDateTime(DateTime.Today))
        {
            ExpiryError = "This date has expired; expired stock cannot be added";
        }

        MrpError = RequiredAmount(MrpText, "MRP per pack", allowZero: true);
        PurchaseRateError = RequiredAmount(PurchaseRateText, "Purchase rate", allowZero: true);
        QuantityError = RequiredAmount(QuantityText, "Quantity (packs)", allowZero: false);
        FreeQuantityError = OptionalAmount(FreeQuantityText, "Free quantity");
        PackSizeError = OptionalAmount(PackSizeText, "Pack size");
        GstError = OptionalAmount(GstText, "GST %");
        if (GstError.Length == 0 && TryAmount(GstText, out var gst) && gst > 100)
        {
            GstError = "GST % cannot be above 100";
        }

        SupplierError = SelectedSupplier is null ? "Supplier is required" : string.Empty;
        SupplierInvoiceNoError = string.IsNullOrWhiteSpace(SupplierInvoiceNo) ? "Supplier invoice no. is required" : string.Empty;
        InvoiceDateError = InvoiceDate is null ? "Invoice date is required" : string.Empty;

        FirstInvalidField = string.Empty;
        var order = new (string Name, string Error)[]
        {
            ("Medicine", MedicineError), ("Schedule", ScheduleError), ("BatchNo", BatchNoError),
            ("Expiry", ExpiryError), ("Mrp", MrpError), ("PurchaseRate", PurchaseRateError),
            ("Quantity", QuantityError), ("FreeQuantity", FreeQuantityError), ("PackSize", PackSizeError),
            ("Gst", GstError), ("Supplier", SupplierError), ("SupplierInvoiceNo", SupplierInvoiceNoError),
            ("InvoiceDate", InvoiceDateError)
        };
        FirstInvalidField = order.FirstOrDefault(item => item.Error.Length > 0).Name ?? string.Empty;
        return FirstInvalidField.Length == 0;
    }

    // Soft rules that need the user's confirmation. Returns false when the user cancels.
    public bool ConfirmWarnings()
    {
        TryParseExpiry(ExpiryText, out var expiry);
        if (expiry > DateOnly.FromDateTime(DateTime.Today).AddYears(5) &&
            !_confirmation.Confirm(
                $"This expiry ({expiry:MM/yyyy}) is more than 5 years away. Is it correct?",
                "Check expiry date"))
        {
            return false;
        }

        TryAmount(MrpText, out var mrp);
        TryAmount(PurchaseRateText, out var rate);
        return mrp >= rate || _confirmation.Confirm(
            $"MRP ({mrp:N2}) is lower than the purchase rate ({rate:N2}). Save anyway?",
            "Check prices");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        if (_scopeFactory is null || _session?.User is null)
        {
            ErrorMessage = "Sign in again to add stock.";
            return;
        }

        if (IsReadOnlyMode)
        {
            ErrorMessage = "Read-only mode: stock entry is blocked until the trial or licence is renewed.";
            return;
        }

        if (!Validate() || !ConfirmWarnings())
        {
            return;
        }

        try
        {
            var input = BuildInput();
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AddStockService>();
            if (await service.BatchExistsAsync(
                    input.DrugId, input.CatalogMedicineId, input.SupplierId, input.BatchNo, input.ExpiryDate) &&
                !_confirmation.Confirm(
                    $"Batch {input.BatchNo} already exists for this supplier and expiry. " +
                    "Add this quantity to the existing batch? Choose No to cancel.",
                    "Batch already exists"))
            {
                return;
            }

            var result = await service.AddStockAsync(input, _session.User.Id, _session.User.Role);
            Saved?.Invoke(this, result);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    public AddStockInput BuildInput()
    {
        TryParseExpiry(ExpiryText, out var expiry);
        TryAmount(MrpText, out var mrp);
        TryAmount(PurchaseRateText, out var rate);
        TryAmount(QuantityText, out var quantity);
        TryAmount(FreeQuantityText, out var free);
        TryAmount(PackSizeText, out var packSize);
        TryAmount(GstText, out var gst);
        return new AddStockInput(
            DrugId,
            CatalogMedicineId,
            MedicineName.Trim(),
            string.IsNullOrWhiteSpace(Composition) ? null : Composition.Trim(),
            string.IsNullOrWhiteSpace(Manufacturer) ? null : Manufacturer.Trim(),
            Schedule!,
            BatchNo.Trim(),
            expiry,
            mrp,
            rate,
            quantity,
            free,
            packSize,
            gst,
            SelectedSupplier!.Id,
            SupplierInvoiceNo.Trim(),
            DateOnly.FromDateTime(InvoiceDate!.Value),
            string.IsNullOrWhiteSpace(Rack) ? null : Rack.Trim());
    }

    // The month/year the user types is valid through the last day of that month.
    public static bool TryParseExpiry(string text, out DateOnly lastDay)
    {
        lastDay = default;
        if (!DateTime.TryParseExact(
                text.Trim(), ExpiryFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        lastDay = new DateOnly(parsed.Year, parsed.Month, DateTime.DaysInMonth(parsed.Year, parsed.Month));
        return true;
    }

    private static bool TryAmount(string text, out decimal value)
    {
        value = 0m;
        return string.IsNullOrWhiteSpace(text) ||
               decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static string RequiredAmount(string text, string label, bool allowZero)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"{label} is required";
        }

        if (!TryAmount(text, out var value) || value < 0 || (!allowZero && value == 0))
        {
            return allowZero ? $"{label} must be a number, 0 or more" : $"{label} must be more than 0";
        }

        return string.Empty;
    }

    private static string OptionalAmount(string text, string label) =>
        string.IsNullOrWhiteSpace(text) || (TryAmount(text, out var value) && value >= 0)
            ? string.Empty
            : $"{label} must be a number, 0 or more";
}
