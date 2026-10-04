using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public enum DrugRecordsPeriod
{
    Today,
    ThisMonth,
    LastMonth,
    LastThreeMonths,
    ThisYear,
    Custom
}

public static class DrugRecordsPeriodRange
{
    public static (DateTime StartUtc, DateTime EndUtc) GetRange(
        DrugRecordsPeriod period,
        DateTime localNow,
        int selectedMonth,
        int selectedYear,
        DateOnly? customStart = null,
        DateOnly? customEndInclusive = null)
    {
        var today = DateOnly.FromDateTime(localNow);
        var selectedMonthStart = new DateOnly(selectedYear, selectedMonth, 1);
        var start = period switch
        {
            DrugRecordsPeriod.Today => today,
            DrugRecordsPeriod.ThisMonth => selectedMonthStart,
            DrugRecordsPeriod.LastMonth => selectedMonthStart.AddMonths(-1),
            DrugRecordsPeriod.LastThreeMonths => selectedMonthStart.AddMonths(-2),
            DrugRecordsPeriod.ThisYear => new DateOnly(selectedYear, 1, 1),
            DrugRecordsPeriod.Custom => customStart
                ?? throw new ArgumentException("Choose a custom start date.", nameof(customStart)),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
        var endExclusive = period switch
        {
            DrugRecordsPeriod.Today => today.AddDays(1),
            DrugRecordsPeriod.ThisMonth => start.AddMonths(1),
            DrugRecordsPeriod.LastMonth => start.AddMonths(1),
            DrugRecordsPeriod.LastThreeMonths => selectedMonthStart.AddMonths(1),
            DrugRecordsPeriod.ThisYear => start.AddYears(1),
            DrugRecordsPeriod.Custom => (customEndInclusive
                ?? throw new ArgumentException("Choose a custom end date.", nameof(customEndInclusive))).AddDays(1),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
        if (period == DrugRecordsPeriod.Custom && endExclusive <= start)
        {
            throw new ArgumentException("The custom end date must not be earlier than the start date.");
        }

        return (start.ToDateTime(TimeOnly.MinValue).ToUniversalTime(),
            endExclusive.ToDateTime(TimeOnly.MinValue).ToUniversalTime());
    }
}

public sealed partial class DrugRecordsPageViewModel : SectionPageViewModel, ILoadablePage
{
    private readonly DrugRecordsService _recordsService;
    private readonly SensitiveAccessService _sensitiveAccess;
    private readonly IConfirmationService _confirmation;
    private readonly IFilePickerService _filePicker;
    private readonly TabularExportService _exportService;
    private readonly CurrentSession _currentSession;
    private bool _authorized;

    public DrugRecordsPageViewModel(
        DrugRecordsService recordsService,
        IFilePickerService filePicker,
        TabularExportService exportService,
        SensitiveAccessService sensitiveAccess,
        IConfirmationService confirmation,
        CurrentSession currentSession)
        : base("Drug Records")
    {
        _recordsService = recordsService;
        _filePicker = filePicker;
        _exportService = exportService;
        _sensitiveAccess = sensitiveAccess;
        _confirmation = confirmation;
        _currentSession = currentSession;
        PeriodOptions = Enum.GetValues<DrugRecordsPeriod>();
        RecordTypeOptions = ["Both", "Sale", "Purchase"];
        MonthOptions = Enumerable.Range(1, 12).ToArray();
        YearOptions = Enumerable.Range(DateTime.Now.Year - 10, 11).Reverse().ToArray();
        SelectedYear = DateTime.Now.Year;
        SelectedMonth = DateTime.Now.Month;
        Period = DrugRecordsPeriod.ThisMonth;
        CustomStartDate = DateTime.Today;
        CustomEndDate = DateTime.Today;
    }

    public IReadOnlyList<DrugRecordsPeriod> PeriodOptions { get; }
    public IReadOnlyList<string> RecordTypeOptions { get; }
    public IReadOnlyList<int> MonthOptions { get; }
    public IReadOnlyList<int> YearOptions { get; }
    public ObservableCollection<DrugRecordsDrugChoiceRow> SearchResults { get; } = [];
    public ObservableCollection<DrugRecordsRow> Records { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private DrugRecordsPeriod _period;

    [ObservableProperty]
    private string _recordType = "Both";

    [ObservableProperty]
    private int _selectedMonth;

    [ObservableProperty]
    private int _selectedYear;

    [ObservableProperty]
    private DateTime? _customStartDate;

    [ObservableProperty]
    private DateTime? _customEndDate;

    [ObservableProperty]
    private bool _hidePatientPhoneNumber;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private decimal _openingBalance;

    [ObservableProperty]
    private decimal _purchased;

    [ObservableProperty]
    private decimal _sold;

    [ObservableProperty]
    private decimal _closingBalance;

    [ObservableProperty]
    private decimal _currentStock;

    [ObservableProperty]
    private int _uniquePatients;

    [ObservableProperty]
    private bool _stockMismatch;

    public bool IsCustomPeriod => Period == DrugRecordsPeriod.Custom;

    partial void OnPeriodChanged(DrugRecordsPeriod value) => OnPropertyChanged(nameof(IsCustomPeriod));

    [RelayCommand]
    private void SetPeriod(string value)
    {
        if (!Enum.TryParse<DrugRecordsPeriod>(value, ignoreCase: true, out var period))
        {
            throw new ArgumentException($"Unsupported Drug Records period '{value}'.", nameof(value));
        }

        Period = period;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _authorized = await _sensitiveAccess.RequestCurrentUserPinAsync(
            Application.Current?.MainWindow,
            "open Drug Records",
            cancellationToken);
        if (!_authorized)
        {
            ErrorMessage = "Access cancelled or PIN verification failed.";
            return;
        }

        StatusMessage = "Access granted. Search a brand or salt and select medicines.";
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SearchDrugsAsync()
    {
        if (!_authorized || _currentSession.User is null)
        {
            ErrorMessage = "Unlock Drug Records before searching.";
            return;
        }

        try
        {
            var results = await _recordsService.SearchDrugsAsync(
                SearchText,
                _currentSession.User.Id);
            SearchResults.Clear();
            foreach (var result in results)
            {
                SearchResults.Add(new DrugRecordsDrugChoiceRow(result));
            }

            StatusMessage = $"{results.Count} matching medicine(s).";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task RunReportAsync()
    {
        if (!_authorized || _currentSession.User is null)
        {
            ErrorMessage = "Unlock Drug Records before running a search.";
            return;
        }

        var selected = SearchResults.Where(item => item.IsSelected).Select(item => item.DrugId).ToArray();
        if (selected.Length == 0)
        {
            ErrorMessage = "Select at least one medicine from the search results.";
            return;
        }

        try
        {
            var range = DrugRecordsPeriodRange.GetRange(
                Period,
                DateTime.Now,
                SelectedMonth,
                SelectedYear,
                CustomStartDate.HasValue ? DateOnly.FromDateTime(CustomStartDate.Value) : null,
                CustomEndDate.HasValue ? DateOnly.FromDateTime(CustomEndDate.Value) : null);
            var report = await _recordsService.SearchAsync(
                new DrugRecordsQuery(selected, range.StartUtc, range.EndUtc, RecordType),
                _currentSession.User.Id);
            Records.Clear();
            foreach (var row in report.Rows)
            {
                Records.Add(row);
            }

            OpeningBalance = report.Summary.OpeningBalance;
            Purchased = report.Summary.Purchased;
            Sold = report.Summary.Sold;
            ClosingBalance = report.Summary.ClosingBalance;
            CurrentStock = report.Summary.CurrentStock;
            UniquePatients = report.Summary.UniquePatients;
            StockMismatch = !report.Summary.ClosingMatchesCurrentStock;
            StatusMessage = $"{report.Rows.Count} record(s) from {report.StartUtc.ToLocalTime():d} through {report.EndUtc.ToLocalTime().AddDays(-1):d}.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private Task ExportPdfAsync() => ExportAsync("pdf");

    [RelayCommand]
    private Task ExportExcelAsync() => ExportAsync("xlsx");

    [RelayCommand]
    private Task PrintPdfAsync() => ExportAsync("pdf", print: true);

    [RelayCommand]
    private Task ShowPdfForSharingAsync() => ExportAsync("pdf", showForSharing: true);

    private async Task ExportAsync(string extension, bool print = false, bool showForSharing = false)
    {
        if (!_authorized || _currentSession.User is null)
        {
            ErrorMessage = "Unlock Drug Records before exporting.";
            return;
        }

        if (!PermissionMatrix.Allows(_currentSession.User.Role, AppPermission.Export))
        {
            ErrorMessage = "Your role cannot export patient Drug Records.";
            return;
        }

        if (!await _sensitiveAccess.RequestCurrentUserPinAsync(
                Application.Current?.MainWindow,
                "export patient drug records"))
        {
            ErrorMessage = "Export cancelled or PIN verification failed.";
            return;
        }

        if (!_confirmation.Confirm(
                "This file contains patient details. Share only with authorised persons.",
                "Confirm sensitive patient data export"))
        {
            return;
        }

        var path = _filePicker.PickExportDestination(extension, "drug-records");
        if (path is null)
        {
            return;
        }

        var columns = new[]
        {
            "Date/time", "Record type", "Document no.", "Patient / buyer", "Address",
            "Phone", "Doctor", "Doctor registration no.", "Batch", "Quantity", "Running balance"
        };
        var summaryRows = new IReadOnlyList<string>[]
        {
            ["Summary: opening balance", "", "", "", "", "", "", "", "", OpeningBalance.ToString("0.##", CultureInfo.InvariantCulture), ""],
            ["Summary: purchased", "", "", "", "", "", "", "", "", Purchased.ToString("0.##", CultureInfo.InvariantCulture), ""],
            ["Summary: sold (net of returns)", "", "", "", "", "", "", "", "", Sold.ToString("0.##", CultureInfo.InvariantCulture), ""],
            ["Summary: closing balance", "", "", "", "", "", "", "", "", ClosingBalance.ToString("0.##", CultureInfo.InvariantCulture), ""],
            ["Summary: current stock", "", "", "", "", "", "", "", "", CurrentStock.ToString("0.##", CultureInfo.InvariantCulture), ""],
            ["Summary: unique patients", "", "", "", "", "", "", "", "", UniquePatients.ToString(CultureInfo.InvariantCulture), ""]
        };
        var detailRows = Records.Select(row => (IReadOnlyList<string>)
        [
            row.AtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture),
            row.RecordType,
            row.DocumentNo,
            row.PatientName,
            row.Address,
            HidePatientPhoneNumber ? string.Empty : row.Phone,
            row.DoctorName,
            row.DoctorRegistrationNo,
            row.BatchNo,
            row.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
            row.RunningBalance.ToString("0.##", CultureInfo.InvariantCulture)
        ]);
        IReadOnlyList<IReadOnlyList<string>> rows = summaryRows.Concat(detailRows).ToArray();
        try
        {
            await _exportService.ExportAsync(path, "Drug Records", columns, rows);
            await _recordsService.LogExportAsync(_currentSession.User.Id, extension, HidePatientPhoneNumber);
            if (print)
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                {
                    UseShellExecute = true,
                    Verb = "print"
                });
                if (process is null)
                {
                    throw new InvalidOperationException("Windows could not open the Drug Records PDF print handler.");
                }
            }
            else if (showForSharing)
            {
                RetailBillPdfService.ShowInExplorer(path);
            }

            StatusMessage = print
                ? $"Drug Records PDF sent to the default print handler: {path}"
                : showForSharing
                    ? $"Drug Records PDF ready to share: {path}"
                    : $"Drug records exported to {path}.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }
}

public sealed partial class DrugRecordsDrugChoiceRow(DrugRecordsDrugChoice drug) : ObservableObject
{
    public Guid DrugId => drug.DrugId;
    public string Name => drug.Name;
    public string? Composition => drug.Composition;
    public string? BrandName => drug.BrandName;

    [ObservableProperty]
    private bool _isSelected;
}
