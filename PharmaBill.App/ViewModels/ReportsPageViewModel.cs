using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed record ReportChoice(ReportKind Kind, string Name);
public sealed record ReportMonth(int Number, string Name);

public partial class ReportsPageViewModel : ObservableObject, ILoadablePage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFilePickerService _filePicker;
    private readonly TabularExportService _exports;

    public ReportsPageViewModel(IServiceScopeFactory scopeFactory, IFilePickerService filePicker, TabularExportService exports)
    {
        _scopeFactory = scopeFactory;
        _filePicker = filePicker;
        _exports = exports;
        var today = DateTime.Today;
        SelectedMonth = today.Month;
        SelectedYear = today.Year;
        FromDate = new DateTime(today.Year, today.Month, 1);
        ToDate = today;
        SelectedReport = ReportChoices[0];
    }

    public ObservableCollection<ReportChoice> ReportChoices { get; } =
    [
        new(ReportKind.Sales, "Sales"),
        new(ReportKind.Purchases, "Purchases"),
        new(ReportKind.Profit, "Profit"),
        new(ReportKind.GstSummary, "GST summary"),
        new(ReportKind.TopSellingDrugs, "Top-selling drugs"),
        new(ReportKind.Expiry, "Expiry"),
        new(ReportKind.LowStock, "Low stock"),
        new(ReportKind.SupplierWise, "Supplier-wise")
    ];

    public ObservableCollection<ReportMonth> Months { get; } =
        new(Enumerable.Range(1, 12).Select(month => new ReportMonth(
            month,
            System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month))));
    public ObservableCollection<int> Years { get; } =
        new(Enumerable.Range(DateTime.Today.Year - 10, 11));

    [ObservableProperty] private ReportChoice _selectedReport;
    [ObservableProperty] private int _selectedMonth;
    [ObservableProperty] private int _selectedYear;
    [ObservableProperty] private DateTime _fromDate;
    [ObservableProperty] private DateTime _toDate;
    [ObservableProperty] private DataView? _table;
    [ObservableProperty] private string _periodLabel = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    public bool CanNavigateNext => SelectedYear < DateTime.Today.Year ||
                                   (SelectedYear == DateTime.Today.Year && SelectedMonth < DateTime.Today.Month);

    partial void OnSelectedMonthChanged(int value)
    {
        if (SelectedYear == DateTime.Today.Year && value > DateTime.Today.Month)
        {
            SelectedMonth = DateTime.Today.Month;
            return;
        }

        if (value is >= 1 and <= 12)
        {
            ApplySelectedMonth();
        }
    }

    partial void OnSelectedYearChanged(int value)
    {
        if (value > DateTime.Today.Year)
        {
            SelectedYear = DateTime.Today.Year;
            return;
        }

        if (value >= Years.First() && value <= DateTime.Today.Year)
        {
            ApplySelectedMonth();
            OnPropertyChanged(nameof(CanNavigateNext));
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RefreshAsync(cancellationToken);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var from = DateOnly.FromDateTime(FromDate);
            var to = DateOnly.FromDateTime(ToDate);
            using var scope = _scopeFactory.CreateScope();
            var report = await scope.ServiceProvider.GetRequiredService<ReportsDashboardService>()
                .GetReportAsync(SelectedReport.Kind, from, to, cancellationToken);
            var table = new DataTable(report.Title);
            foreach (var column in report.Columns)
            {
                table.Columns.Add(column);
            }

            foreach (var row in report.Rows)
            {
                table.Rows.Add(row.Cast<object>().ToArray());
            }

            Table = table.DefaultView;
            PeriodLabel = $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
            StatusMessage = $"{report.Rows.Count} row(s).";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task SelectQuickPeriodAsync(string period)
    {
        var today = DateTime.Today;
        switch (period)
        {
            case "Today":
                FromDate = today;
                ToDate = today;
                break;
            case "This month":
                SelectedMonth = today.Month;
                SelectedYear = today.Year;
                FromDate = new DateTime(today.Year, today.Month, 1);
                ToDate = today;
                break;
            case "Last month":
                SetMonth(today.AddMonths(-1));
                ToDate = new DateTime(SelectedYear, SelectedMonth, DateTime.DaysInMonth(SelectedYear, SelectedMonth));
                break;
            case "Last 3 months":
                FromDate = new DateTime(today.Year, today.Month, 1).AddMonths(-2);
                ToDate = today;
                break;
            case "This year":
                FromDate = new DateTime(today.Year, 1, 1);
                ToDate = today;
                break;
            default:
                throw new ArgumentException("Unknown report period chip.", nameof(period));
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        SetMonth(new DateTime(SelectedYear, SelectedMonth, 1).AddMonths(-1));
        await RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateNext))]
    private async Task NextMonthAsync()
    {
        if (!CanNavigateNext)
        {
            return;
        }

        SetMonth(new DateTime(SelectedYear, SelectedMonth, 1).AddMonths(1));
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ExportPdfAsync() => await ExportAsync("pdf");

    [RelayCommand]
    private async Task ExportExcelAsync() => await ExportAsync("xlsx");

    private async Task ExportAsync(string extension)
    {
        if (Table is null)
        {
            await RefreshAsync();
        }

        if (Table is null)
        {
            return;
        }

        var rows = Table.Cast<DataRowView>()
            .Select(row => (IReadOnlyList<string>)row.Row.ItemArray.Select(item => Convert.ToString(item) ?? string.Empty).ToArray())
            .ToArray();
        var baseName = $"{SelectedReport.Name.Replace(' ', '-')}-{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}";
        var destination = _filePicker.PickExportDestination(extension, baseName);
        if (destination is null)
        {
            return;
        }

        var columns = Table.Table?.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray()
            ?? throw new InvalidOperationException("The report has no table columns.");
        await _exports.ExportAsync(destination, $"{SelectedReport.Name} ({PeriodLabel})", columns, rows);
        StatusMessage = $"Exported {rows.Length} row(s) to {destination}.";
        ErrorMessage = string.Empty;
    }

    private void ApplySelectedMonth()
    {
        if (SelectedYear <= 0 || SelectedMonth is < 1 or > 12)
        {
            return;
        }

        var month = new DateTime(SelectedYear, SelectedMonth, 1);
        FromDate = month;
        ToDate = SelectedYear == DateTime.Today.Year && SelectedMonth == DateTime.Today.Month
            ? DateTime.Today
            : month.AddMonths(1).AddDays(-1);
        OnPropertyChanged(nameof(CanNavigateNext));
        NextMonthCommand.NotifyCanExecuteChanged();
    }

    private void SetMonth(DateTime value)
    {
        if (value > new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1))
        {
            value = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        }

        SelectedYear = value.Year;
        SelectedMonth = value.Month;
        OnPropertyChanged(nameof(CanNavigateNext));
        NextMonthCommand.NotifyCanExecuteChanged();
    }
}
