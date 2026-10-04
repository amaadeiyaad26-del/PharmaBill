using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace PharmaBill.App.ViewModels;

public sealed partial class RetailBillingViewModel(
    IServiceScopeFactory scopeFactory,
    CurrentSession currentSession,
    IFilePickerService filePicker,
    IConfirmationService confirmationService,
    CatalogSearchViewModel catalogSearch,
    RetailBillPdfService pdfService) : ObservableObject, ILoadablePage
{
    [ObservableProperty]
    private string _billNumber = "Assigned on save";

    [ObservableProperty]
    private DateTime _billDate = DateTime.Now;

    [ObservableProperty]
    private string _patientName = string.Empty;

    [ObservableProperty]
    private string _patientPhone = string.Empty;

    [ObservableProperty]
    private string _patientAddress = string.Empty;

    [ObservableProperty]
    private string _doctorName = string.Empty;

    [ObservableProperty]
    private string _doctorRegistrationNumber = string.Empty;

    [ObservableProperty]
    private string _patientNameError = string.Empty;

    [ObservableProperty]
    private string _patientPhoneError = string.Empty;

    [ObservableProperty]
    private string _patientAddressError = string.Empty;

    [ObservableProperty]
    private string _doctorNameError = string.Empty;

    [ObservableProperty]
    private string _doctorRegistrationError = string.Empty;

    [ObservableProperty]
    private string _upiReference = string.Empty;

    [ObservableProperty]
    private string _creditNote = string.Empty;

    [ObservableProperty]
    private string? _prescriptionDocumentPath;

    [ObservableProperty]
    private string _prescriptionFileName = "No prescription attached";

    [ObservableProperty]
    private string _itemSearch = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _paymentMethod = "Cash";

    [ObservableProperty]
    private decimal _paymentAmount;

    [ObservableProperty]
    private decimal _cashTendered;

    [ObservableProperty]
    private bool _upiPaymentReceived;

    [ObservableProperty]
    private string _upiId = string.Empty;

    [ObservableProperty]
    private string _upiLink = string.Empty;

    [ObservableProperty]
    private ImageSource? _upiQrCode;

    [ObservableProperty]
    private string _changeDueText = string.Empty;

    [ObservableProperty]
    private string _focusRequest = string.Empty;

    [ObservableProperty]
    private RetailStockChoice? _selectedSearchResult;

    [ObservableProperty]
    private RetailBillLineViewModel? _selectedBillLine;

    [ObservableProperty]
    private HeldRetailBill? _selectedHeldBill;

    [ObservableProperty]
    private MedicineSearchResult? _selectedOutOfStockMedicine;

    public ObservableCollection<RetailStockChoice> SearchResults { get; } = [];
    public ObservableCollection<RetailBillLineViewModel> BillItems { get; } = [];
    public ObservableCollection<MedicineSearchResult> Substitutes => catalogSearch.SubstituteResults;
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasSubstitutes => Substitutes.Count > 0;
    public IReadOnlyList<string> PaymentMethods { get; } = ["Cash", "UPI", "Card", "Credit"];
    public ObservableCollection<HeldRetailBill> HeldBills { get; } = [];
    private string? _pharmacyName;

    public decimal Subtotal => BillItems.Sum(line => line.NetAmount);
    public decimal GstAmount => BillItems.Sum(line => line.TaxAmount);
    public decimal DiscountTotal => BillItems.Sum(line => line.DiscountAmount);
    public decimal TotalAmount => BillItems.Sum(line => line.GrossAmount);
    public bool IsUpiPayment => PaymentMethod == "UPI";
    public bool HasControlledItems => BillItems.Any(line => line.RequiresPrescription);
    public bool HasPrescription => !string.IsNullOrWhiteSpace(PrescriptionDocumentPath);
    public bool IsCashPayment => PaymentMethod == "Cash";
    public bool IsCreditPayment => PaymentMethod == "Credit";
    public bool IsPaymentAmountVisible => PaymentMethod != "Credit";
    public string ChangeDueValue => MoneyFormat.Number(ChangeDue);
    private decimal ChangeDue { get; set; }
    public bool HasHabitFormingItems => BillItems.Any(line => line.IsHabitForming);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var profile = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>()
            .PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
        _pharmacyName = profile.Name;
        UpiId = profile.UpiId ?? string.Empty;
        BillNumber = await scope.ServiceProvider.GetRequiredService<RetailBillingService>()
            .PreviewNextInvoiceNoAsync(cancellationToken);
    }

    partial void OnPrescriptionDocumentPathChanged(string? value) => OnPropertyChanged(nameof(HasPrescription));
    partial void OnPatientNameChanged(string value) => PatientNameError = string.Empty;
    partial void OnPatientPhoneChanged(string value) => PatientPhoneError = string.Empty;
    partial void OnPatientAddressChanged(string value) => PatientAddressError = string.Empty;
    partial void OnDoctorNameChanged(string value) => DoctorNameError = string.Empty;
    partial void OnDoctorRegistrationNumberChanged(string value) => DoctorRegistrationError = string.Empty;

    partial void OnPaymentMethodChanged(string value)
    {
        OnPropertyChanged(nameof(IsCashPayment));
        OnPropertyChanged(nameof(IsCreditPayment));
        OnPropertyChanged(nameof(IsPaymentAmountVisible));
        UpdateChangeDue();
        OnPropertyChanged(nameof(IsUpiPayment));
        RefreshUpiLink();
    }

    partial void OnItemSearchChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            OnPropertyChanged(nameof(HasSearchResults));
        }
    }

    partial void OnPaymentAmountChanged(decimal value)
    {
        RefreshUpiLink();
        UpdateChangeDue();
    }
    partial void OnCashTenderedChanged(decimal value) => UpdateChangeDue();
    partial void OnSelectedOutOfStockMedicineChanged(MedicineSearchResult? value) => OnPropertyChanged(nameof(Substitutes));

    [RelayCommand]
    private async Task SearchItemsAsync()
    {
        ErrorMessage = string.Empty;
        SearchResults.Clear();
        OnPropertyChanged(nameof(HasSearchResults));
        if (string.IsNullOrWhiteSpace(ItemSearch))
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
            var results = await service.SearchStockAsync(ItemSearch);
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }

            var exactBarcode = results.FirstOrDefault(result =>
                string.Equals(result.Barcode, ItemSearch.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exactBarcode is not null)
            {
                AddStockChoice(exactBarcode);
                return;
            }

            if (results.Count > 0)
            {
                AddStockChoice(results[0]);
                return;
            }

            if (results.Count == 0)
            {
                var catalog = scope.ServiceProvider.GetRequiredService<CatalogSearchService>();
                var matches = await catalog.SearchAsync(ItemSearch);
                SelectedOutOfStockMedicine = matches.FromCatalog.FirstOrDefault();
                if (SelectedOutOfStockMedicine is not null)
                {
                    await catalogSearch.ShowSubstitutesCommand.ExecuteAsync(SelectedOutOfStockMedicine);
                    OnPropertyChanged(nameof(Substitutes));
                    OnPropertyChanged(nameof(HasSubstitutes));
                    StatusMessage = "No saleable stock found. Review substitutes; pharmacist must confirm suitability.";
                }
                else
                {
                    StatusMessage = "No saleable batch found.";
                }
            }
            else
            {
                SelectedSearchResult = results[0];
                FocusRequest = "SearchResults";
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private void PickFirstSearchResult()
    {
        if (SelectedSearchResult is not null)
        {
            AddStockChoice(SelectedSearchResult);
        }
        else if (SearchResults.Count > 0)
        {
            AddStockChoice(SearchResults[0]);
        }
    }

    [RelayCommand]
    private async Task AddFromSubstituteAsync(MedicineSearchResult? medicine)
    {
        if (medicine is null)
        {
            return;
        }

        ItemSearch = medicine.Name;
        await SearchItemsAsync();
        if (SearchResults.Count == 0)
        {
            ErrorMessage = "This catalogue substitute has no saleable batch in local stock.";
        }
    }

    [RelayCommand]
    private async Task ShowSubstitutesAsync()
    {
        if (SelectedOutOfStockMedicine is null)
        {
            ErrorMessage = "Search for an out-of-stock medicine first.";
            return;
        }

        await catalogSearch.ShowSubstitutesCommand.ExecuteAsync(SelectedOutOfStockMedicine);
        OnPropertyChanged(nameof(Substitutes));
        OnPropertyChanged(nameof(HasSubstitutes));
    }

    [RelayCommand]
    private void AttachPrescription()
    {
        var path = filePicker.PickPrescriptionDocument();
        if (path is null)
        {
            return;
        }

        PrescriptionDocumentPath = path;
        PrescriptionFileName = Path.GetFileName(path);
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private void OpenCamera()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo("microsoft.windows.camera:")
            {
                UseShellExecute = true
            });
            if (process is null)
            {
                throw new InvalidOperationException("Windows could not open the Camera app.");
            }

            StatusMessage = "Capture the prescription with Camera, then use Prescription file to attach the saved photo.";
            ErrorMessage = string.Empty;
        }
        catch (Win32Exception exception)
        {
            ErrorMessage = $"Windows could not open the Camera app: {exception.Message}";
        }
    }

    [RelayCommand]
    private void FocusSearch() => RequestFocus("ItemSearch");

    [RelayCommand]
    private void FocusPatient() => RequestFocus("PatientName");

    [RelayCommand]
    private void FocusPayment() => RequestFocus("PaymentAmount");

    [RelayCommand]
    private void NewBill()
    {
        if (BillItems.Count > 0 &&
            !confirmationService.Confirm("Clear the current unsaved bill?", "New bill"))
        {
            return;
        }

        ClearBill();
    }

    [RelayCommand]
    private void HoldBill()
    {
        if (BillItems.Count == 0)
        {
            ErrorMessage = "Add at least one item before holding a bill.";
            return;
        }

        if (HeldBills.Count >= 10)
        {
            ErrorMessage = "Up to 10 bills can be held at a time.";
            return;
        }

        HeldBills.Add(new HeldRetailBill(
            $"Held {DateTime.Now:HH:mm:ss} - {PatientName}",
            PatientName,
            PatientPhone,
            PatientAddress,
            DoctorName,
            DoctorRegistrationNumber,
            PrescriptionDocumentPath,
            BillItems.Select(line => line.ToSnapshot()).ToArray()));
        ClearBill();
        StatusMessage = $"{HeldBills.Count} bill(s) held. Resume them from the held-bill list.";
    }

    [RelayCommand]
    private void ResumeBill(HeldRetailBill? bill)
    {
        if (bill is null)
        {
            ErrorMessage = "Select a held bill to resume.";
            return;
        }

        if (BillItems.Count > 0 &&
            !confirmationService.Confirm("Hold the current bill before resuming the selected bill?", "Resume held bill"))
        {
            return;
        }

        if (BillItems.Count > 0)
        {
            if (HeldBills.Count >= 10)
            {
                ErrorMessage = "There is no room to hold the current bill. Resume after saving or removing a held bill.";
                return;
            }

            HoldBill();
        }

        HeldBills.Remove(bill);
        PatientName = bill.PatientName;
        PatientPhone = bill.PatientPhone;
        PatientAddress = bill.PatientAddress;
        DoctorName = bill.DoctorName;
        DoctorRegistrationNumber = bill.DoctorRegistrationNumber;
        PrescriptionDocumentPath = bill.PrescriptionDocumentPath;
        PrescriptionFileName = bill.PrescriptionDocumentPath is null
            ? "No prescription attached"
            : Path.GetFileName(bill.PrescriptionDocumentPath);
        foreach (var snapshot in bill.Items)
        {
            var line = RetailBillLineViewModel.FromSnapshot(snapshot);
            line.PropertyChanged += OnBillLineChanged;
            BillItems.Add(line);
        }

        UpdateTotals();
    }

    [RelayCommand]
    private void CancelBill()
    {
        if (BillItems.Count > 0 && !confirmationService.Confirm("Cancel the unsaved bill?", "Cancel bill"))
        {
            return;
        }

        ClearBill();
    }

    [RelayCommand]
    private void RemoveSelectedItem()
    {
        if (SelectedBillLine is null)
        {
            return;
        }

        BillItems.Remove(SelectedBillLine);
        SelectedBillLine = null;
        UpdateTotals();
    }

    [RelayCommand]
    private void ResumeSelectedBill() => ResumeBill(SelectedHeldBill);

    [RelayCommand]
    private async Task SaveAndPrintAsync()
    {
        ErrorMessage = string.Empty;
        if (currentSession.User is null)
        {
            ErrorMessage = "Sign in again before saving a bill.";
            return;
        }

        if (!ValidateRequiredFields())
        {
            return;
        }

        if (BillItems.Count == 0)
        {
            ErrorMessage = "Add at least one medicine to the bill.";
            return;
        }

        if (PaymentMethod == "UPI" && (string.IsNullOrWhiteSpace(UpiId) || string.IsNullOrWhiteSpace(UpiLink)))
        {
            ErrorMessage = "Enter the pharmacy UPI ID in Settings and ensure the payment QR is available.";
            return;
        }

        foreach (var line in BillItems)
        {
            var validation = line.Validate();
            if (validation is not null)
            {
                SelectedBillLine = line;
                ErrorMessage = validation;
                return;
            }
        }

        try
        {
            var payments = BuildPaymentInputs();
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
            var result = await service.SaveSaleAsync(
                new SaveRetailSaleInput(
                    PatientName,
                    PatientPhone,
                    PatientAddress,
                    DoctorName,
                    DoctorRegistrationNumber,
                    PrescriptionDocumentPath,
                    BillItems.Select(line => line.ToInput()).ToArray(),
                    payments,
                    UpiPaymentReceived),
                currentSession.User.Id,
                currentSession.User.Role);
            BillNumber = result.InvoiceNo;
            ChangeDueText = result.ChangeDue > 0 ? $"Change due: {MoneyFormat.Rupees(result.ChangeDue)}" : string.Empty;
            StatusMessage = $"Bill {result.InvoiceNo} saved. Total {MoneyFormat.Rupees(result.TotalAmount)}; paid {MoneyFormat.Rupees(result.PaidAmount)}.";
            string? printFailure = null;
            try
            {
                await pdfService.PrintAsync(result.Sale.Id);
            }
            catch (Exception printException)
            {
                printFailure = $"Bill {result.InvoiceNo} was saved, but printing failed: {printException.Message}";
            }

            ClearBill();
            BillNumber = await service.PreviewNextInvoiceNoAsync();
            StatusMessage = $"Bill {result.InvoiceNo} saved. Use Recent bills to reprint or share its PDF.";
            ErrorMessage = printFailure ?? string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private bool ValidateRequiredFields()
    {
        PatientNameError = string.IsNullOrWhiteSpace(PatientName) ? "Patient name is required" : string.Empty;
        PatientPhoneError = string.IsNullOrWhiteSpace(PatientPhone) ? "Phone number is required" : string.Empty;
        var controlled = HasControlledItems;
        PatientAddressError = controlled && string.IsNullOrWhiteSpace(PatientAddress)
            ? "Address is required for H1, X and NDPS items" : string.Empty;
        DoctorNameError = controlled && string.IsNullOrWhiteSpace(DoctorName)
            ? "Doctor name is required for H1, X and NDPS items" : string.Empty;
        DoctorRegistrationError = controlled && string.IsNullOrWhiteSpace(DoctorRegistrationNumber)
            ? "Registration no. is required for H1, X and NDPS items" : string.Empty;

        var firstMissing = new (string Error, string Target)[]
        {
            (PatientNameError, "PatientName"),
            (PatientPhoneError, "PatientPhone"),
            (PatientAddressError, "PatientAddress"),
            (DoctorNameError, "DoctorName"),
            (DoctorRegistrationError, "DoctorRegistration")
        }.FirstOrDefault(entry => entry.Error.Length > 0);
        if (firstMissing.Target is null)
        {
            return true;
        }

        ErrorMessage = firstMissing.Error;
        RequestFocus(firstMissing.Target);
        return false;
    }

    private void RequestFocus(string target)
    {
        FocusRequest = string.Empty;
        FocusRequest = target;
    }

    private IReadOnlyList<RetailPaymentInput> BuildPaymentInputs()
    {
        if (PaymentMethod == "Credit" || PaymentAmount <= 0)
        {
            return [];
        }

        var applied = Math.Min(PaymentAmount, TotalAmount);
        var tendered = PaymentMethod == "Cash" && CashTendered > 0 ? CashTendered : applied;
        return [new RetailPaymentInput(PaymentMethod, applied, tendered)];
    }

    private void AddStockChoice(RetailStockChoice choice)
    {
        var existing = BillItems.FirstOrDefault(line => line.Choice.BatchId == choice.BatchId);
        if (existing is not null)
        {
            if (existing.Quantity + 1 > choice.AvailableQuantity)
            {
                ErrorMessage = $"Only {choice.AvailableQuantity} units are available in batch {choice.BatchNo}.";
                return;
            }

            existing.Quantity += 1;
        }
        else
        {
            var line = new RetailBillLineViewModel(choice);
            line.PropertyChanged += OnBillLineChanged;
            BillItems.Add(line);
            SelectedBillLine = line;
        }

        ErrorMessage = string.Empty;
        StatusMessage = choice.IsHabitForming
            ? "Habit-forming (reference data): verify prescription and register requirements."
            : string.Empty;
        ItemSearch = string.Empty;
        SearchResults.Clear();
        SelectedSearchResult = null;
        OnPropertyChanged(nameof(HasSearchResults));
        OnPropertyChanged(nameof(HasControlledItems));
        OnPropertyChanged(nameof(HasHabitFormingItems));
        UpdateTotals();
        RequestFocus("ItemSearch");
    }

    private void OnBillLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateTotals();
        if (sender is RetailBillLineViewModel line && line.Validate() is { } validation)
        {
            ErrorMessage = validation;
        }
        else
        {
            ErrorMessage = string.Empty;
        }
    }

    private void UpdateTotals()
    {
        OnPropertyChanged(nameof(Subtotal));
        OnPropertyChanged(nameof(GstAmount));
        OnPropertyChanged(nameof(DiscountTotal));
        OnPropertyChanged(nameof(TotalAmount));
        OnPropertyChanged(nameof(HasControlledItems));
        OnPropertyChanged(nameof(HasHabitFormingItems));
        if (PaymentAmount <= 0 || PaymentAmount > TotalAmount)
        {
            PaymentAmount = TotalAmount;
        }

        RefreshUpiLink();
        UpdateChangeDue();
    }

    private void UpdateChangeDue()
    {
        var tendered = CashTendered > 0 ? CashTendered : PaymentAmount;
        var change = PaymentMethod == "Cash"
            ? RetailTaxCalculator.RoundMoney(Math.Max(0m, tendered - Math.Min(PaymentAmount, TotalAmount)))
            : 0m;
        ChangeDue = change;
        OnPropertyChanged(nameof(ChangeDueValue));
        ChangeDueText = change > 0 ? $"Change due: {MoneyFormat.Rupees(change)}" : string.Empty;
    }

    private void RefreshUpiLink()
    {
        if (PaymentMethod != "UPI" || string.IsNullOrWhiteSpace(UpiId) || PaymentAmount <= 0)
        {
            UpiLink = string.Empty;
            UpiQrCode = null;
            return;
        }

        var amount = RetailTaxCalculator.RoundMoney(Math.Min(PaymentAmount, TotalAmount))
            .ToString("0.00", CultureInfo.InvariantCulture);
        UpiLink = $"upi://pay?pa={Uri.EscapeDataString(UpiId)}" +
                  $"&pn={Uri.EscapeDataString(_pharmacyName ?? "Pharmacy")}" +
                  $"&am={amount}&cu=INR&tn={Uri.EscapeDataString(BillNumber)}";
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Width = 600,
                Height = 600,
                Margin = 4,
                ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.M,
                CharacterSet = "UTF-8"
            }
        };
        var pixels = writer.Write(UpiLink);
        var bitmap = BitmapSource.Create(
            pixels.Width,
            pixels.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels.Pixels,
            pixels.Width * 4);
        bitmap.Freeze();
        UpiQrCode = bitmap;
    }

    private void ClearFieldState()
    {
        PatientNameError = PatientPhoneError = PatientAddressError = DoctorNameError = DoctorRegistrationError = string.Empty;
        UpiReference = CreditNote = string.Empty;
    }

    private void ClearBill()
    {
        PatientName = string.Empty;
        PatientPhone = string.Empty;
        PatientAddress = string.Empty;
        DoctorName = string.Empty;
        DoctorRegistrationNumber = string.Empty;
        PrescriptionDocumentPath = null;
        PrescriptionFileName = "No prescription attached";
        ItemSearch = string.Empty;
        SearchResults.Clear();
        OnPropertyChanged(nameof(HasSearchResults));
        BillItems.Clear();
        PaymentMethod = "Cash";
        PaymentAmount = 0;
        CashTendered = 0;
        UpiPaymentReceived = false;
        BillDate = DateTime.Now;
        ErrorMessage = string.Empty;
        ChangeDueText = string.Empty;
        ClearFieldState();
        UpdateTotals();
        RequestFocus("PatientName");
    }
}

public sealed partial class RetailBillLineViewModel : ObservableObject
{
    public RetailBillLineViewModel(RetailStockChoice choice)
    {
        Choice = choice;
        UnitPrice = choice.SalePrice.HasValue &&
                    choice.SalePrice.Value >= 0 &&
                    choice.SalePrice.Value <= choice.Mrp
            ? choice.SalePrice.Value
            : choice.Mrp;
    }

    private RetailBillLineViewModel(RetailBillLineSnapshot snapshot)
    {
        Choice = snapshot.Choice;
        Quantity = snapshot.Quantity;
        DiscountAmount = snapshot.DiscountAmount;
        UnitPrice = snapshot.UnitPrice;
    }

    public RetailStockChoice Choice { get; }
    public string DrugName => Choice.DrugName;
    public string BatchNo => Choice.BatchNo;
    public DateOnly? ExpiryDate => Choice.ExpiryDate;
    public decimal Mrp => Choice.Mrp;
    public decimal StockQuantity => Choice.AvailableQuantity;
    public decimal GstRate => Choice.GstRate;
    public bool RequiresPrescription => Choice.Schedule is "H1" or "X" or "NDPS";
    public bool IsHabitForming => Choice.IsHabitForming;
    public string? Schedule => Choice.Schedule;

    [ObservableProperty]
    private decimal _quantity = 1m;

    [ObservableProperty]
    private decimal _discountAmount;

    [ObservableProperty]
    private decimal _unitPrice;

    public decimal GrossAmount => TaxSplit.GrossAmount;
    public decimal TaxAmount => TaxSplit.TaxAmount;
    public decimal NetAmount => GrossAmount - TaxAmount;

    private InclusiveTaxLine TaxSplit =>
        Quantity <= 0 ||
        UnitPrice < 0 ||
        GstRate < 0 ||
        DiscountAmount < 0 ||
        DiscountAmount > Quantity * UnitPrice
            ? default
            : RetailTaxCalculator.SplitInclusive(Quantity, UnitPrice, GstRate, DiscountAmount);

    public string? Validate()
    {
        if (Quantity <= 0)
        {
            return "Quantity must be greater than zero.";
        }

        if (Quantity > Choice.AvailableQuantity)
        {
            return $"Quantity for {DrugName} exceeds live stock ({Choice.AvailableQuantity}).";
        }

        if (Choice.ExpiryDate.HasValue && Choice.ExpiryDate.Value < DateOnly.FromDateTime(DateTime.Today))
        {
            return $"Batch {BatchNo} is expired.";
        }

        if (UnitPrice < 0 || UnitPrice > Mrp)
        {
            return $"Price for {DrugName} cannot exceed MRP {MoneyFormat.Rupees(Mrp)}.";
        }

        if (Mrp <= 0)
        {
            return $"A valid MRP is required for {DrugName}.";
        }

        if (DiscountAmount < 0 || DiscountAmount > Quantity * UnitPrice)
        {
            return "Discount cannot be negative or exceed the line amount.";
        }

        return null;
    }

    public RetailSaleLineInput ToInput() =>
        new(Choice.DrugId, Choice.BatchId, Quantity, DiscountAmount, UnitPrice);

    public RetailBillLineSnapshot ToSnapshot() => new(Choice, Quantity, DiscountAmount, UnitPrice);

    public static RetailBillLineViewModel FromSnapshot(RetailBillLineSnapshot snapshot) => new(snapshot);

    partial void OnQuantityChanged(decimal value) => UpdateAmounts();
    partial void OnDiscountAmountChanged(decimal value) => UpdateAmounts();
    partial void OnUnitPriceChanged(decimal value) => UpdateAmounts();

    private void UpdateAmounts()
    {
        OnPropertyChanged(nameof(GrossAmount));
        OnPropertyChanged(nameof(TaxAmount));
        OnPropertyChanged(nameof(NetAmount));
    }
}

public sealed record RetailBillLineSnapshot(
    RetailStockChoice Choice,
    decimal Quantity,
    decimal DiscountAmount,
    decimal UnitPrice);

public sealed record HeldRetailBill(
    string DisplayName,
    string PatientName,
    string PatientPhone,
    string PatientAddress,
    string DoctorName,
    string DoctorRegistrationNumber,
    string? PrescriptionDocumentPath,
    IReadOnlyList<RetailBillLineSnapshot> Items);
