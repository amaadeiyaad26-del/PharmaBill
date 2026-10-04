using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public partial class WholesaleReportsPageViewModel : ObservableObject, ILoadablePage
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFilePickerService _filePicker;
    private readonly TabularExportService _exports;

    public WholesaleReportsPageViewModel(
        IServiceScopeFactory scopeFactory,
        IFilePickerService filePicker,
        TabularExportService exports)
    {
        _scopeFactory = scopeFactory;
        _filePicker = filePicker;
        _exports = exports;
        FromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToDate = DateTime.Today;
    }

    public ObservableCollection<WholesaleSalesRegisterRow> SalesRegister { get; } = [];
    public ObservableCollection<WholesalePurchaseRegisterRow> PurchaseRegister { get; } = [];
    public ObservableCollection<GstRateSummary> HsnSummary { get; } = [];
    public ObservableCollection<WholesaleSalesAnalysisRow> Analysis { get; } = [];

    [ObservableProperty] private DateTime _fromDate;
    [ObservableProperty] private DateTime _toDate;
    [ObservableProperty] private string _analysisDimension = "customer";
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _gstr3bNotice = "For reference only; verify with your CA.";
    [ObservableProperty] private string _gstr3bFigures = string.Empty;

    public string[] AnalysisDimensions { get; } = ["customer", "item", "company", "salesman", "area"];

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RefreshAsync(cancellationToken);

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reports = scope.ServiceProvider.GetRequiredService<WholesaleGstReportsService>();
            var from = DateOnly.FromDateTime(FromDate);
            var to = DateOnly.FromDateTime(ToDate);
            var sales = await reports.GetSalesRegisterAsync(from, to, cancellationToken);
            var purchases = await reports.GetPurchaseRegisterAsync(from, to, cancellationToken);
            var hsn = await reports.GetHsnSummaryAsync(from, to, cancellationToken);
            var analysis = await reports.GetSalesAnalysisAsync(from, to, AnalysisDimension, cancellationToken);
            var summary = await reports.GetGstr3bReferenceSummaryAsync(from, to, cancellationToken);
            Replace(SalesRegister, sales);
            Replace(PurchaseRegister, purchases);
            Replace(HsnSummary, hsn);
            Replace(Analysis, analysis);
            Gstr3bFigures = $"Taxable {MoneyFormat.Rupees(summary.Taxable)}; CGST {MoneyFormat.Rupees(summary.Cgst)}; SGST {MoneyFormat.Rupees(summary.Sgst)}; IGST {MoneyFormat.Rupees(summary.Igst)}";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ExportGstr1CsvAsync() => await ExportGstr1Async("csv");

    [RelayCommand]
    private async Task ExportGstr1JsonAsync() => await ExportGstr1Async("json");

    [RelayCommand]
    private async Task ExportSalesRegisterAsync()
    {
        var path = _filePicker.PickExportDestination("xlsx", "Wholesale-sales-register");
        if (path is null)
        {
            return;
        }

        var rows = SalesRegister.Select(row => (IReadOnlyList<string>)
        [
            row.InvoiceNo,
            row.InvoiceAtUtc.ToLocalTime().ToString("yyyy-MM-dd"),
            row.CustomerName,
            row.Gstin ?? string.Empty,
            row.Hsn,
            row.TaxableAmount.ToString("0.00", CultureInfo.InvariantCulture),
            row.Cgst.ToString("0.00", CultureInfo.InvariantCulture),
            row.Sgst.ToString("0.00", CultureInfo.InvariantCulture),
            row.Igst.ToString("0.00", CultureInfo.InvariantCulture),
            row.Total.ToString("0.00", CultureInfo.InvariantCulture)
        ]).ToArray();
        await _exports.ExportAsync(path, "Wholesale sales register",
            ["Invoice", "Date", "Customer", "GSTIN", "HSN", "Taxable", "CGST", "SGST", "IGST", "Total"], rows);
        StatusMessage = path;
    }

    private async Task ExportGstr1Async(string extension)
    {
        var path = _filePicker.PickExportDestination(extension, $"GSTR1-reference-{FromDate:yyyyMMdd}-{ToDate:yyyyMMdd}");
        if (path is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<WholesaleGstReportsService>();
        var content = extension == "csv"
            ? await reports.ExportGstr1CsvAsync(DateOnly.FromDateTime(FromDate), DateOnly.FromDateTime(ToDate))
            : await reports.ExportGstr1JsonAsync(DateOnly.FromDateTime(FromDate), DateOnly.FromDateTime(ToDate));
        await File.WriteAllTextAsync(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        StatusMessage = path;
        ErrorMessage = string.Empty;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }
}
