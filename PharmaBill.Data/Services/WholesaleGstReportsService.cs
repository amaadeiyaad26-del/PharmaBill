using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record WholesaleSalesRegisterRow(
    string InvoiceNo,
    DateTime InvoiceAtUtc,
    string CustomerName,
    string? Gstin,
    string Hsn,
    decimal TaxableAmount,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Total);

public sealed record GstRateSummary(
    string Hsn,
    decimal TaxRate,
    decimal Taxable,
    decimal Cgst,
    decimal Sgst,
    decimal Igst,
    decimal Total);

public sealed record Gstr3bReferenceSummary(
    string Notice,
    decimal Taxable,
    decimal Cgst,
    decimal Sgst,
    decimal Igst);

public sealed record WholesaleSalesAnalysisRow(
    string Group,
    decimal Quantity,
    decimal Sales,
    decimal Cost,
    decimal Margin);

public sealed record WholesalePurchaseRegisterRow(
    string InvoiceNo,
    DateOnly InvoiceDate,
    string SupplierName,
    string? Gstin,
    string Hsn,
    decimal TaxableAmount,
    decimal TaxAmount,
    decimal Total);

public sealed class WholesaleGstReportsService(PharmaBillDbContext context)
{
    public async Task<IReadOnlyList<WholesalePurchaseRegisterRow>> GetPurchaseRegisterAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(from, to);
        var invoices = await context.PurchaseInvoices.AsNoTracking()
            .Where(invoice => invoice.Status == "Posted" &&
                              invoice.InvoiceDate >= from && invoice.InvoiceDate <= to)
            .ToListAsync(cancellationToken);
        var invoiceIds = invoices.Select(invoice => invoice.Id).ToArray();
        var suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken);
        var items = await context.PurchaseItems.AsNoTracking()
            .Where(item => invoiceIds.Contains(item.PurchaseInvoiceId))
            .ToListAsync(cancellationToken);
        var drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken);
        return items.Select(item =>
            {
                var invoice = invoices.Single(row => row.Id == item.PurchaseInvoiceId);
                var supplier = suppliers[invoice.SupplierId];
                var taxable = decimal.Round(
                    item.Quantity * item.UnitPrice - item.DiscountAmount,
                    2,
                    MidpointRounding.AwayFromZero);
                var tax = decimal.Round(taxable * item.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
                return new WholesalePurchaseRegisterRow(
                    invoice.InvoiceNo,
                    invoice.InvoiceDate,
                    supplier.Name,
                    supplier.Gstin,
                    drugs[item.DrugId].HsnCode ?? string.Empty,
                    taxable,
                    tax,
                    item.LineTotal);
            })
            .OrderBy(row => row.InvoiceDate)
            .ThenBy(row => row.InvoiceNo)
            .ToArray();
    }

    public async Task<IReadOnlyList<WholesaleSalesRegisterRow>> GetSalesRegisterAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(from, to);
        var invoices = await context.WholesaleInvoices.AsNoTracking()
            .Where(invoice => invoice.Status == "Posted")
            .ToListAsync(cancellationToken);
        var invoiceIds = invoices.Where(invoice =>
                DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()) >= from &&
                DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()) <= to)
            .Select(invoice => invoice.Id).ToArray();
        var customers = await context.Customers.AsNoTracking()
            .ToDictionaryAsync(customer => customer.Id, cancellationToken);
        var items = await context.WholesaleInvoiceItems.AsNoTracking()
            .Where(item => invoiceIds.Contains(item.WholesaleInvoiceId))
            .ToListAsync(cancellationToken);
        var drugs = await context.Drugs.AsNoTracking()
            .ToDictionaryAsync(drug => drug.Id, cancellationToken);
        var grouped = items.GroupBy(item => item.WholesaleInvoiceId);
        var rows = new List<WholesaleSalesRegisterRow>();
        foreach (var group in grouped)
        {
            var invoice = invoices.Single(item => item.Id == group.Key);
            var customer = customers[invoice.CustomerId];
            foreach (var line in group)
            {
                rows.Add(new WholesaleSalesRegisterRow(
                    invoice.InvoiceNo,
                    invoice.InvoiceAtUtc,
                    customer.Name,
                    customer.Gstin,
                    drugs[line.DrugId].HsnCode ?? string.Empty,
                    line.TaxableAmount,
                    line.CgstAmount,
                    line.SgstAmount,
                    line.IgstAmount,
                    line.LineTotal));
            }
        }

        return rows.OrderBy(row => row.InvoiceAtUtc).ThenBy(row => row.InvoiceNo).ToArray();
    }

    public async Task<IReadOnlyList<GstRateSummary>> GetHsnSummaryAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(from, to);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var invoiceIds = await context.WholesaleInvoices.AsNoTracking()
            .Where(invoice => invoice.Status == "Posted" &&
                              invoice.InvoiceAtUtc >= fromUtc &&
                              invoice.InvoiceAtUtc < toUtcExclusive)
            .Select(invoice => invoice.Id)
            .ToArrayAsync(cancellationToken);
        var items = await context.WholesaleInvoiceItems.AsNoTracking()
            .Where(item => invoiceIds.Contains(item.WholesaleInvoiceId))
            .ToListAsync(cancellationToken);
        var drugIds = items.Select(item => item.DrugId).Distinct().ToArray();
        var drugs = await context.Drugs.AsNoTracking()
            .Where(drug => drugIds.Contains(drug.Id))
            .ToDictionaryAsync(drug => drug.Id, cancellationToken);
        return items.GroupBy(item => new
            {
                Hsn = drugs[item.DrugId].HsnCode ?? string.Empty,
                item.TaxRate
            })
            .Select(group => new GstRateSummary(
                group.Key.Hsn,
                group.Key.TaxRate,
                group.Sum(item => item.TaxableAmount),
                group.Sum(item => item.CgstAmount),
                group.Sum(item => item.SgstAmount),
                group.Sum(item => item.IgstAmount),
                group.Sum(item => item.LineTotal)))
            .OrderBy(row => row.Hsn, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.TaxRate)
            .ToArray();
    }

    public async Task<Gstr3bReferenceSummary> GetGstr3bReferenceSummaryAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var rows = await GetSalesRegisterAsync(from, to, cancellationToken);
        return new Gstr3bReferenceSummary(
            "For reference only; verify with your CA.",
            rows.Sum(row => row.TaxableAmount),
            rows.Sum(row => row.Cgst),
            rows.Sum(row => row.Sgst),
            rows.Sum(row => row.Igst));
    }

    public async Task<string> ExportGstr1JsonAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var rows = await GetSalesRegisterAsync(from, to, cancellationToken);
        var invoices = rows.GroupBy(row => row.InvoiceNo).Select(group => new
        {
            invoiceNo = group.Key,
            date = DateOnly.FromDateTime(group.First().InvoiceAtUtc.ToLocalTime()).ToString("yyyy-MM-dd"),
            customer = group.First().CustomerName,
            gstin = group.First().Gstin,
            items = group.Select(row => new
            {
                hsn = row.Hsn,
                taxable = row.TaxableAmount,
                cgst = row.Cgst,
                sgst = row.Sgst,
                igst = row.Igst,
                total = row.Total
            })
        });
        return JsonSerializer.Serialize(new
        {
            format = "GSTR-1-style reference export",
            warning = "Not a government filing format. Verify with your CA before use.",
            from,
            to,
            invoices
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> ExportGstr1CsvAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var rows = await GetSalesRegisterAsync(from, to, cancellationToken);
        var builder = new StringBuilder("InvoiceNo,InvoiceDate,Customer,GSTIN,HSN,Taxable,CGST,SGST,IGST,Total\r\n");
        foreach (var row in rows)
        {
            var fields = new[]
            {
                row.InvoiceNo,
                DateOnly.FromDateTime(row.InvoiceAtUtc.ToLocalTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.CustomerName,
                row.Gstin ?? string.Empty,
                row.Hsn,
                row.TaxableAmount.ToString("0.00", CultureInfo.InvariantCulture),
                row.Cgst.ToString("0.00", CultureInfo.InvariantCulture),
                row.Sgst.ToString("0.00", CultureInfo.InvariantCulture),
                row.Igst.ToString("0.00", CultureInfo.InvariantCulture),
                row.Total.ToString("0.00", CultureInfo.InvariantCulture)
            };
            builder.AppendLine(string.Join(",", fields.Select(EscapeCsv)));
        }

        return builder.ToString();
    }

    public async Task<IReadOnlyList<WholesaleSalesAnalysisRow>> GetSalesAnalysisAsync(
        DateOnly from,
        DateOnly to,
        string dimension,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(from, to);
        var invoices = await context.WholesaleInvoices.AsNoTracking()
            .Where(invoice => invoice.Status == "Posted")
            .ToListAsync(cancellationToken);
        var relevantInvoices = invoices.Where(invoice =>
            DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()) >= from &&
            DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()) <= to).ToArray();
        var invoiceIds = relevantInvoices.Select(invoice => invoice.Id).ToArray();
        var invoiceById = relevantInvoices.ToDictionary(invoice => invoice.Id);
        var customers = await context.Customers.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken);
        var items = await context.WholesaleInvoiceItems.AsNoTracking()
            .Where(item => invoiceIds.Contains(item.WholesaleInvoiceId))
            .ToListAsync(cancellationToken);
        var drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken);
        var batches = await context.Batches.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken);
        var catalogIds = drugs.Values.Where(drug => drug.CatalogMedicineId.HasValue)
            .Select(drug => drug.CatalogMedicineId!.Value).Distinct().ToArray();
        var manufacturers = await context.CatalogMedicines.AsNoTracking()
            .Where(item => catalogIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Manufacturer ?? "Unknown company", cancellationToken);
        var grouped = items.GroupBy(item => GetDimension(
            dimension,
            invoiceById[item.WholesaleInvoiceId],
            customers,
            drugs[item.DrugId],
            manufacturers));
        return grouped.Select(group =>
        {
            var sales = group.Sum(item => item.LineTotal);
            var cost = group.Sum(item => batches[item.BatchId].PurchasePrice * (item.Quantity + item.FreeQuantity));
            return new WholesaleSalesAnalysisRow(
                group.Key,
                group.Sum(item => item.Quantity + item.FreeQuantity),
                sales,
                cost,
                sales - cost);
        }).OrderByDescending(row => row.Sales).ToArray();
    }

    private static string GetDimension(
        string dimension,
        PharmaBill.Core.Entities.WholesaleInvoice invoice,
        IReadOnlyDictionary<Guid, PharmaBill.Core.Entities.Customer> customers,
        PharmaBill.Core.Entities.Drug drug,
        IReadOnlyDictionary<Guid, string> manufacturers) =>
        dimension.ToLowerInvariant() switch
        {
            "customer" => customers[invoice.CustomerId].Name,
            "item" => drug.Name,
            "company" => drug.CatalogMedicineId.HasValue
                ? manufacturers.GetValueOrDefault(drug.CatalogMedicineId.Value, "Unknown company")
                : "Unknown company",
            "salesman" => customers[invoice.CustomerId].Salesman ?? "Unassigned",
            "area" => customers[invoice.CustomerId].Route ?? "Unassigned",
            _ => throw new ArgumentException("Dimension must be customer, item, company, salesman or area.", nameof(dimension))
        };

    private static string EscapeCsv(string value) =>
        $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static void ValidatePeriod(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException("Report end date cannot precede its start date.");
        }
    }
}
