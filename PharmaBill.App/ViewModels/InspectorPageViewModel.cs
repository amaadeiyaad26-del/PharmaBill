using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.ViewModels;

public sealed class InspectorPageViewModel(
    IServiceScopeFactory scopeFactory,
    SensitiveAccessService sensitiveAccess) : SectionPageViewModel("Inspector view"), ILoadablePage
{
    public DataView? Licences { get; private set; }
    public DataView? Stock { get; private set; }
    public DataView? Registers { get; private set; }
    public DataView? Invoices { get; private set; }
    public string StatusMessage { get; private set; } = "Read-only inspector view.";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!await sensitiveAccess.RequestInspectorPinAsync(
                System.Windows.Application.Current?.MainWindow,
                cancellationToken))
        {
            StatusMessage = "Inspector access cancelled or PIN verification failed.";
            OnPropertyChanged(nameof(StatusMessage));
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var mode = await context.PharmacyProfiles.AsNoTracking()
            .Select(item => item.BusinessMode)
            .SingleAsync(cancellationToken);
        var wholesaleOnly = mode == BusinessMode.Wholesaler;
        var licences = await context.LicenceRecords.AsNoTracking()
            .OrderBy(item => item.LicenceType).ToListAsync(cancellationToken);
        Licences = ToView(
            ["Licence type", "Number", "Issue date", "Expiry", "Document"],
            licences.Select(item => new[]
            {
                item.LicenceType, item.LicenceNumber, item.IssuedOn?.ToString("d") ?? string.Empty,
                item.ExpiresOn?.ToString("d") ?? string.Empty, item.DocumentPath ?? string.Empty
            }));

        var batches = await context.Batches.AsNoTracking().OrderBy(item => item.ExpiryDate).ToListAsync(cancellationToken);
        var batchIds = batches.Select(item => item.Id).ToArray();
        var drugIds = batches.Select(item => item.DrugId).Distinct().ToArray();
        var drugs = await context.Drugs.AsNoTracking().Where(item => drugIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var stockMovements = await context.StockMovements.AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId))
            .ToListAsync(cancellationToken);
        var movementTotals = stockMovements.GroupBy(item => item.BatchId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.QuantityChange));
        Stock = ToView(
            ["Drug", "Batch", "Expiry", "Current quantity", "MRP", "Rack"],
            batches.Select(item => new[]
            {
                drugs.GetValueOrDefault(item.DrugId, string.Empty), item.BatchNo,
                item.ExpiryDate?.ToString("d") ?? string.Empty,
                movementTotals.GetValueOrDefault(item.Id).ToString("0.##"),
                item.Mrp?.ToString("N2") ?? string.Empty, item.Rack ?? string.Empty
            }));

        var registers = await context.ScheduleRegisterEntries.AsNoTracking()
            .OrderByDescending(item => item.EntryAtUtc).Take(5000).ToListAsync(cancellationToken);
        Registers = ToView(
            ["Date", "Register", "Patient / buyer", "Drug", "Batch", "Quantity", "Reason"],
            registers.Select(item => new[]
            {
                item.EntryAtUtc.ToLocalTime().ToString("g"), item.RegisterType, wholesaleOnly ? string.Empty : item.PatientName ?? string.Empty,
                item.DrugId is { } drugId ? drugs.GetValueOrDefault(drugId, string.Empty) : string.Empty,
                item.BatchNo ?? string.Empty, item.Quantity?.ToString("0.##") ?? string.Empty, item.Notes ?? string.Empty
            }));

        var sales = await context.Sales.AsNoTracking().OrderByDescending(item => item.SaleAtUtc).Take(5000).ToListAsync(cancellationToken);
        var wholesale = await context.WholesaleInvoices.AsNoTracking().OrderByDescending(item => item.InvoiceAtUtc).Take(5000).ToListAsync(cancellationToken);
        var purchaseInvoices = await context.PurchaseInvoices.AsNoTracking().OrderByDescending(item => item.InvoiceDate).Take(5000).ToListAsync(cancellationToken);
        var patientIds = sales.Where(item => item.PatientId.HasValue).Select(item => item.PatientId!.Value).Distinct().ToArray();
        var patients = await context.Patients.AsNoTracking().Where(item => patientIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var customers = await context.Customers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        var invoiceRows = sales.Select(item => new[]
            {
                item.SaleAtUtc.ToLocalTime().ToString("g"), "Retail sale", item.InvoiceNo,
                !wholesaleOnly && item.PatientId.HasValue
                    ? patients.GetValueOrDefault(item.PatientId.Value, string.Empty)
                    : string.Empty,
                item.TotalAmount.ToString("N2")
            })
            .Concat(wholesale.Select(item => new[]
            {
                item.InvoiceAtUtc.ToLocalTime().ToString("g"), "Wholesale invoice", item.InvoiceNo,
                customers.GetValueOrDefault(item.CustomerId, string.Empty), item.TotalAmount.ToString("N2")
            }))
            .Concat(purchaseInvoices.Select(item => new[]
            {
                item.InvoiceDate.ToString("d"), "Purchase invoice", item.InvoiceNo,
                suppliers.GetValueOrDefault(item.SupplierId, string.Empty), item.TotalAmount.ToString("N2")
            }))
            .OrderByDescending(item => item[0], StringComparer.Ordinal)
            .Take(5000);
        Invoices = ToView(["Date", "Type", "Document no.", "Party", "Total"], invoiceRows);
        StatusMessage = $"Read-only inspector view loaded. {_columnsCount(Licences)} licence(s), {batches.Count} batches, {registers.Count} register rows.";
        OnPropertyChanged(nameof(Licences));
        OnPropertyChanged(nameof(Stock));
        OnPropertyChanged(nameof(Registers));
        OnPropertyChanged(nameof(Invoices));
        OnPropertyChanged(nameof(StatusMessage));
    }

    private static int _columnsCount(DataView? view) => view?.Count ?? 0;

    private static DataView ToView(string[] columns, IEnumerable<string[]> rows)
    {
        var table = new DataTable();
        foreach (var column in columns)
        {
            table.Columns.Add(column);
        }

        foreach (var row in rows)
        {
            table.Rows.Add(row.Cast<object>().ToArray());
        }

        return table.DefaultView;
    }
}
