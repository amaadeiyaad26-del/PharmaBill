using System.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.ViewModels;

public partial class OutputDataPageViewModel : SectionPageViewModel, ILoadablePage
{
    private readonly string _sectionKey;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFilePickerService _filePickerService;
    private readonly TabularExportService _exportService;
    private string[] _columns = [];
    private IReadOnlyList<string[]> _rows = [];

    public OutputDataPageViewModel(
        string sectionKey,
        string title,
        IServiceScopeFactory scopeFactory,
        IFilePickerService filePickerService,
        TabularExportService exportService)
        : base(title)
    {
        _sectionKey = sectionKey;
        _scopeFactory = scopeFactory;
        _filePickerService = filePickerService;
        _exportService = exportService;
    }

    [ObservableProperty]
    private DataView? _table;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            if (_sectionKey == "Registers")
            {
                var entries = await context.ScheduleRegisterEntries.AsNoTracking()
                    .OrderByDescending(item => item.EntryAtUtc)
                    .Take(5000)
                    .ToListAsync(cancellationToken);
                var writeOffs = await context.ExpiryWriteOffs.AsNoTracking()
                    .OrderByDescending(item => item.WrittenOffAtUtc)
                    .Take(5000)
                    .ToListAsync(cancellationToken);
                var drugIds = entries.Where(item => item.DrugId.HasValue)
                    .Select(item => item.DrugId!.Value)
                    .Concat(writeOffs.Select(item => item.DrugId))
                    .Distinct().ToArray();
                var drugNames = await context.Drugs.AsNoTracking()
                    .Where(item => drugIds.Contains(item.Id))
                    .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
                var batchIds = writeOffs.Select(item => item.BatchId).Distinct().ToArray();
                var batchNumbers = await context.Batches.AsNoTracking()
                    .Where(item => batchIds.Contains(item.Id))
                    .ToDictionaryAsync(item => item.Id, item => item.BatchNo, cancellationToken);
                _columns = ["Entry date", "Register", "Medicine", "Batch", "Quantity", "Patient", "Prescriber", "Registration no.", "Notes / reason"];
                _rows = entries.Select(item => (
                        At: item.EntryAtUtc,
                        Values: new[]
                        {
                            item.EntryAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                            item.RegisterType,
                            item.DrugId.HasValue ? drugNames.GetValueOrDefault(item.DrugId.Value, string.Empty) : string.Empty,
                            item.BatchNo ?? string.Empty,
                            item.Quantity?.ToString("0.##") ?? string.Empty,
                            item.PatientName ?? string.Empty,
                            item.PrescriberName ?? string.Empty,
                            item.PrescriberRegistrationNumber ?? string.Empty,
                            item.Notes ?? string.Empty
                        }))
                    .Concat(writeOffs.Select(item => (
                        At: item.WrittenOffAtUtc,
                        Values: new[]
                        {
                            item.WrittenOffAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                            "Expiry dump",
                            drugNames.GetValueOrDefault(item.DrugId, string.Empty),
                            batchNumbers.GetValueOrDefault(item.BatchId, string.Empty),
                            item.Quantity.ToString("0.##"),
                            string.Empty,
                            string.Empty,
                            string.Empty,
                            item.Reason
                        })))
                    .OrderByDescending(item => item.At)
                    .Take(5000)
                    .Select(item => item.Values)
                    .ToArray();
            }
            else
            {
                var businessMode = await context.PharmacyProfiles.AsNoTracking()
                    .Select(item => item.BusinessMode)
                    .SingleAsync(cancellationToken);
                var sales = businessMode == BusinessMode.Wholesaler
                    ? []
                    : await context.Sales.AsNoTracking()
                        .OrderByDescending(item => item.SaleAtUtc)
                        .Take(5000)
                        .ToListAsync(cancellationToken);
                var wholesaleInvoices = await context.WholesaleInvoices.AsNoTracking()
                    .OrderByDescending(item => item.InvoiceAtUtc)
                    .Take(5000)
                    .ToListAsync(cancellationToken);
                var patientIds = sales.Where(item => item.PatientId.HasValue)
                    .Select(item => item.PatientId!.Value).Distinct().ToArray();
                var patients = await context.Patients.AsNoTracking()
                    .Where(item => patientIds.Contains(item.Id))
                    .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
                var customerIds = wholesaleInvoices.Select(item => item.CustomerId).Distinct().ToArray();
                var customers = await context.Customers.AsNoTracking()
                    .Where(item => customerIds.Contains(item.Id))
                    .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
                _columns = businessMode == BusinessMode.Wholesaler
                    ? ["Date", "Invoice no.", "Customer", "Subtotal", "GST", "Discount", "Total", "Paid", "Status"]
                    : ["Date", "Invoice no.", "Patient / customer", "Subtotal", "GST", "Discount", "Total", "Paid", "Status"];
                _rows = sales.Select(item => (
                        At: item.SaleAtUtc,
                        Values: new[]
                        {
                            item.SaleAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                            item.InvoiceNo,
                            item.PatientId.HasValue ? patients.GetValueOrDefault(item.PatientId.Value, string.Empty) : string.Empty,
                            item.Subtotal.ToString("N2"),
                            item.TaxAmount.ToString("N2"),
                            item.DiscountAmount.ToString("N2"),
                            item.TotalAmount.ToString("N2"),
                            item.PaidAmount.ToString("N2"),
                            item.PaymentStatus ?? string.Empty
                        }))
                    .Concat(wholesaleInvoices.Select(item => (
                        At: item.InvoiceAtUtc,
                        Values: new[]
                        {
                            item.InvoiceAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"),
                            item.InvoiceNo,
                            customers.GetValueOrDefault(item.CustomerId, string.Empty),
                            item.Subtotal.ToString("N2"),
                            item.TaxAmount.ToString("N2"),
                            item.DiscountAmount.ToString("N2"),
                            item.TotalAmount.ToString("N2"),
                            item.PaidAmount.ToString("N2"),
                            item.PaymentStatus ?? string.Empty
                        })))
                    .OrderByDescending(item => item.At)
                    .Take(5000)
                    .Select(item => item.Values)
                    .ToArray();
            }

            BuildTable();
            StatusMessage = $"{_rows.Count} row(s) loaded. Showing at most 5,000 records.";
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
    private Task ExportCsvAsync() => ExportAsync("csv");

    private async Task ExportAsync(string extension)
    {
        var path = _filePickerService.PickExportDestination(extension, _sectionKey.ToLowerInvariant());
        if (path is null)
        {
            return;
        }

        try
        {
            await _exportService.ExportAsync(path, Title, _columns, _rows, CancellationToken.None);
            StatusMessage = $"Exported {_rows.Count} row(s) to {path}.";
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    private void BuildTable()
    {
        var table = new DataTable();
        foreach (var column in _columns)
        {
            table.Columns.Add(column);
        }

        foreach (var values in _rows)
        {
            table.Rows.Add(values.Cast<object>().ToArray());
        }

        Table = table.DefaultView;
    }
}
