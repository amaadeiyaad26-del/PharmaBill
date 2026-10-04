using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public enum ReportKind
{
    Sales,
    Purchases,
    Profit,
    GstSummary,
    TopSellingDrugs,
    Expiry,
    LowStock,
    SupplierWise
}

public sealed record ReportTable(
    string Title,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record DashboardSnapshot(
    decimal TodaySales,
    decimal MonthSales,
    int ExpiringSoonBatches,
    int LowStockDrugs,
    decimal WholesaleOutstanding,
    int RegisterAlerts);

public sealed class ReportsDashboardService(PharmaBillDbContext context)
{
    public async Task<DashboardSnapshot> GetDashboardAsync(
        DateTime nowLocal,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(nowLocal);
        var todayStartUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var tomorrowUtc = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthStartUtc = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var sales = await context.Sales.AsNoTracking()
            .Where(item => item.SaleAtUtc >= todayStartUtc && item.SaleAtUtc < tomorrowUtc)
            .SumAsync(item => (decimal?)item.TotalAmount, cancellationToken) ?? 0m;
        var wholesaleToday = await context.WholesaleInvoices.AsNoTracking()
            .Where(item => item.Status == "Posted" && item.InvoiceAtUtc >= todayStartUtc && item.InvoiceAtUtc < tomorrowUtc)
            .SumAsync(item => (decimal?)item.TotalAmount, cancellationToken) ?? 0m;
        var monthRetail = await context.Sales.AsNoTracking()
            .Where(item => item.SaleAtUtc >= monthStartUtc && item.SaleAtUtc < tomorrowUtc)
            .SumAsync(item => (decimal?)item.TotalAmount, cancellationToken) ?? 0m;
        var monthWholesale = await context.WholesaleInvoices.AsNoTracking()
            .Where(item => item.Status == "Posted" && item.InvoiceAtUtc >= monthStartUtc && item.InvoiceAtUtc < tomorrowUtc)
            .SumAsync(item => (decimal?)item.TotalAmount, cancellationToken) ?? 0m;
        var threshold = today.AddDays(90);
        var expiredSoon = await context.Batches.AsNoTracking()
            .Where(batch => batch.ExpiryDate.HasValue &&
                            batch.ExpiryDate.Value >= today &&
                            batch.ExpiryDate.Value <= threshold)
            .Select(batch => batch.Id)
            .ToArrayAsync(cancellationToken);
        var balances = await context.StockMovements.AsNoTracking()
            .GroupBy(item => item.BatchId)
            .Select(group => new { BatchId = group.Key, Quantity = group.Sum(item => item.QuantityChange) })
            .ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
        var nearExpiry = await context.Batches.AsNoTracking()
            .Where(batch => expiredSoon.Contains(batch.Id))
            .ToListAsync(cancellationToken);
        var expiringSoonCount = nearExpiry.Count(batch => balances.GetValueOrDefault(batch.Id) > 0m);
        var drugs = await context.Drugs.AsNoTracking().Where(drug => drug.IsActive).ToListAsync(cancellationToken);
        var quantities = await (from batch in context.Batches.AsNoTracking()
                                join movement in context.StockMovements.AsNoTracking()
                                    on batch.Id equals movement.BatchId
                                group movement by batch.DrugId
            into grouped
                                select new { DrugId = grouped.Key, Quantity = grouped.Sum(item => item.QuantityChange) })
            .ToDictionaryAsync(item => item.DrugId, item => item.Quantity, cancellationToken);
        var lowStock = drugs.Count(drug =>
            drug.ReorderLevel > 0 && quantities.GetValueOrDefault(drug.Id) < drug.ReorderLevel);
        var outstanding = await GetWholesaleOutstandingAsync(today, cancellationToken);
        var recentRegisterAlerts = await context.ScheduleRegisterEntries.AsNoTracking()
            .CountAsync(item => item.EntryAtUtc >= todayStartUtc && item.EntryAtUtc < tomorrowUtc, cancellationToken);
        return new DashboardSnapshot(
            sales + wholesaleToday,
            monthRetail + monthWholesale,
            expiringSoonCount,
            lowStock,
            outstanding,
            recentRegisterAlerts);
    }

    public async Task<ReportTable> GetReportAsync(
        ReportKind kind,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("Report end date cannot precede its start date.");
        }

        return kind switch
        {
            ReportKind.Sales => await SalesAsync(from, to, cancellationToken),
            ReportKind.Purchases => await PurchasesAsync(from, to, cancellationToken),
            ReportKind.Profit => await ProfitAsync(from, to, cancellationToken),
            ReportKind.GstSummary => await GstAsync(from, to, cancellationToken),
            ReportKind.TopSellingDrugs => await TopSellingAsync(from, to, cancellationToken),
            ReportKind.Expiry => await ExpiryAsync(from, to, cancellationToken),
            ReportKind.LowStock => await LowStockAsync(cancellationToken),
            ReportKind.SupplierWise => await SupplierWiseAsync(from, to, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private async Task<ReportTable> SalesAsync(DateOnly from, DateOnly to, CancellationToken token)
    {
        var (startUtc, endUtc) = GetUtcBounds(from, to);
        var retail = await context.Sales.AsNoTracking()
            .Where(sale => sale.SaleAtUtc >= startUtc && sale.SaleAtUtc < endUtc)
            .ToListAsync(token);
        var wholesale = await context.WholesaleInvoices.AsNoTracking()
            .Where(invoice => invoice.Status == "Posted" && invoice.InvoiceAtUtc >= startUtc && invoice.InvoiceAtUtc < endUtc)
            .ToListAsync(token);
        var patients = await context.Patients.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var customers = await context.Customers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var rows = retail.Select(sale => Row(
                sale.SaleAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), sale.InvoiceNo,
                sale.PatientId.HasValue ? patients.GetValueOrDefault(sale.PatientId.Value, string.Empty) : string.Empty,
                sale.TotalAmount, sale.PaidAmount, sale.TaxAmount))
            .Concat(wholesale.Select(invoice => Row(
                invoice.InvoiceAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), invoice.InvoiceNo,
                customers.GetValueOrDefault(invoice.CustomerId, string.Empty),
                invoice.TotalAmount, invoice.PaidAmount, invoice.TaxAmount)))
            .OrderBy(row => row[0], StringComparer.Ordinal)
            .ToArray();
        return new ReportTable("Sales", ["Date/time", "Invoice", "Patient / buyer", "Total", "Paid", "Tax"], rows);
    }

    private async Task<ReportTable> PurchasesAsync(DateOnly from, DateOnly to, CancellationToken token)
    {
        var invoices = await context.PurchaseInvoices.AsNoTracking()
            .Where(item => item.Status == "Posted" && item.InvoiceDate >= from && item.InvoiceDate <= to)
            .ToListAsync(token);
        var suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var rows = invoices.OrderBy(item => item.InvoiceDate).Select(invoice =>
            (IReadOnlyList<string>)[
                invoice.InvoiceDate.ToString("yyyy-MM-dd"),
                invoice.InvoiceNo,
                suppliers.GetValueOrDefault(invoice.SupplierId, string.Empty),
                Format(invoice.Subtotal),
                Format(invoice.TaxAmount),
                Format(invoice.TotalAmount)
            ]).ToArray();
        return new ReportTable("Purchases", ["Date", "Invoice", "Supplier", "Subtotal", "GST", "Total"], rows);
    }

    private async Task<ReportTable> ProfitAsync(DateOnly from, DateOnly to, CancellationToken token)
    {
        var (startUtc, endUtc) = GetUtcBounds(from, to);
        var retailSales = await context.Sales.AsNoTracking()
            .Where(item => item.SaleAtUtc >= startUtc && item.SaleAtUtc < endUtc)
            .ToListAsync(token);
        var wholesaleSales = await context.WholesaleInvoices.AsNoTracking()
            .Where(item => item.Status == "Posted" && item.InvoiceAtUtc >= startUtc && item.InvoiceAtUtc < endUtc)
            .ToListAsync(token);
        var retailIds = retailSales.Select(item => item.Id).ToArray();
        var wholesaleIds = wholesaleSales.Select(item => item.Id).ToArray();
        var retailItems = await context.SaleItems.AsNoTracking().Where(item => retailIds.Contains(item.SaleId)).ToListAsync(token);
        var wholesaleItems = await context.WholesaleInvoiceItems.AsNoTracking()
            .Where(item => wholesaleIds.Contains(item.WholesaleInvoiceId)).ToListAsync(token);
        var drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var batches = await context.Batches.AsNoTracking().ToDictionaryAsync(item => item.Id, token);
        var grouped = retailItems.Select(item => (
                item.DrugId, Qty: item.Quantity, Revenue: item.LineTotal,
                Cost: batches[item.BatchId].PurchasePrice * item.Quantity))
            .Concat(wholesaleItems.Select(item => (
                item.DrugId, Qty: item.Quantity + item.FreeQuantity, Revenue: item.LineTotal,
                Cost: batches[item.BatchId].PurchasePrice * (item.Quantity + item.FreeQuantity))))
            .GroupBy(item => item.DrugId)
            .Select(group =>
            {
                var revenue = group.Sum(item => item.Revenue);
                var cost = group.Sum(item => item.Cost);
                return (IReadOnlyList<string>)[
                    drugs.GetValueOrDefault(group.Key, "Unknown"),
                    group.Sum(item => item.Qty).ToString("0.##"),
                    Format(revenue),
                    Format(cost),
                    Format(revenue - cost)
                ];
            })
            .OrderByDescending(row => decimal.Parse(row[4], System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        return new ReportTable("Profit by drug (estimate at recorded purchase rate)",
            ["Drug", "Qty", "Sales", "Purchase cost", "Gross margin"], grouped);
    }

    private async Task<ReportTable> GstAsync(DateOnly from, DateOnly to, CancellationToken token)
    {
        var (startUtc, endUtc) = GetUtcBounds(from, to);
        var invoices = await context.WholesaleInvoices.AsNoTracking()
            .Where(item => item.Status == "Posted" && item.InvoiceAtUtc >= startUtc && item.InvoiceAtUtc < endUtc)
            .ToListAsync(token);
        var invoiceIds = invoices.Select(item => item.Id).ToArray();
        var wholesaleItems = await context.WholesaleInvoiceItems.AsNoTracking()
            .Where(item => invoiceIds.Contains(item.WholesaleInvoiceId)).ToListAsync(token);
        var retailIds = await context.Sales.AsNoTracking()
            .Where(item => item.SaleAtUtc >= startUtc && item.SaleAtUtc < endUtc)
            .Select(item => item.Id).ToArrayAsync(token);
        var retailItems = await context.SaleItems.AsNoTracking()
            .Where(item => retailIds.Contains(item.SaleId)).ToListAsync(token);
        var drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(item => item.Id, token);
        var wholesaleGroups = wholesaleItems.GroupBy(item => (Hsn: drugs[item.DrugId].HsnCode ?? string.Empty, item.TaxRate))
            .Select(group => new
            {
                group.Key.Hsn,
                group.Key.TaxRate,
                Taxable = group.Sum(item => item.TaxableAmount),
                Cgst = group.Sum(item => item.CgstAmount),
                Sgst = group.Sum(item => item.SgstAmount),
                Igst = group.Sum(item => item.IgstAmount),
                Total = group.Sum(item => item.LineTotal)
            });
        var retailGroups = retailItems.GroupBy(item => (Hsn: drugs[item.DrugId].HsnCode ?? string.Empty, item.TaxRate))
            .Select(group =>
            {
                var total = group.Sum(item => item.LineTotal);
                var taxable = decimal.Round(total / (1m + group.Key.TaxRate / 100m), 2, MidpointRounding.AwayFromZero);
                var tax = total - taxable;
                var halfTax = decimal.Round(tax / 2m, 2, MidpointRounding.AwayFromZero);
                return new
                {
                    group.Key.Hsn,
                    group.Key.TaxRate,
                    Taxable = taxable,
                    Cgst = halfTax,
                    Sgst = tax - halfTax,
                    Igst = 0m,
                    Total = total
                };
            });
        var rows = wholesaleGroups.Concat(retailGroups)
            .GroupBy(item => (item.Hsn, item.TaxRate))
            .Select(group => (IReadOnlyList<string>)[
                group.Key.Hsn,
                Format(group.Key.TaxRate),
                Format(group.Sum(item => item.Taxable)),
                Format(group.Sum(item => item.Cgst)),
                Format(group.Sum(item => item.Sgst)),
                Format(group.Sum(item => item.Igst)),
                Format(group.Sum(item => item.Total))
            ]).ToArray();
        return new ReportTable("GST summary (wholesale invoices)",
            ["HSN", "Rate %", "Taxable", "CGST", "SGST", "IGST", "Total"], rows);
    }

    private async Task<ReportTable> TopSellingAsync(DateOnly from, DateOnly to, CancellationToken token)
    {
        var (startUtc, endUtc) = GetUtcBounds(from, to);
        var sales = await context.Sales.AsNoTracking()
            .Where(item => item.SaleAtUtc >= startUtc && item.SaleAtUtc < endUtc)
            .Select(item => item.Id).ToArrayAsync(token);
        var wholesale = await context.WholesaleInvoices.AsNoTracking()
            .Where(item => item.Status == "Posted" && item.InvoiceAtUtc >= startUtc && item.InvoiceAtUtc < endUtc)
            .Select(item => item.Id).ToArrayAsync(token);
        var retailItems = await context.SaleItems.AsNoTracking().Where(item => sales.Contains(item.SaleId)).ToListAsync(token);
        var wholesaleItems = await context.WholesaleInvoiceItems.AsNoTracking()
            .Where(item => wholesale.Contains(item.WholesaleInvoiceId)).ToListAsync(token);
        var drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var rows = retailItems.Select(item => (item.DrugId, item.Quantity, item.LineTotal))
            .Concat(wholesaleItems.Select(item => (item.DrugId, Quantity: item.Quantity + item.FreeQuantity, item.LineTotal)))
            .GroupBy(item => item.DrugId)
            .Select(group => (IReadOnlyList<string>)[
                drugs.GetValueOrDefault(group.Key, "Unknown"),
                group.Sum(item => item.Quantity).ToString("0.##"),
                Format(group.Sum(item => item.LineTotal))
            ])
            .OrderByDescending(row => decimal.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        return new ReportTable("Top-selling drugs", ["Drug", "Quantity", "Sales"], rows);
    }

    private async Task<ReportTable> ExpiryAsync(DateOnly from, DateOnly to, CancellationToken token)
    {
        var batches = await context.Batches.AsNoTracking()
            .Where(item => item.ExpiryDate.HasValue && item.ExpiryDate.Value >= from && item.ExpiryDate.Value <= to)
            .ToListAsync(token);
        var ids = batches.Select(item => item.Id).ToArray();
        var stock = await context.StockMovements.AsNoTracking().Where(item => ids.Contains(item.BatchId))
            .GroupBy(item => item.BatchId)
            .Select(group => new { Id = group.Key, Qty = group.Sum(item => item.QuantityChange) })
            .ToDictionaryAsync(item => item.Id, item => item.Qty, token);
        var drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var rows = batches.Select(batch => (Batch: batch, Qty: stock.GetValueOrDefault(batch.Id)))
            .Where(item => item.Qty > 0)
            .OrderBy(item => item.Batch.ExpiryDate)
            .Select(item => (IReadOnlyList<string>)[
                drugs.GetValueOrDefault(item.Batch.DrugId, "Unknown"),
                item.Batch.BatchNo,
                item.Batch.ExpiryDate!.Value.ToString("yyyy-MM-dd"),
                item.Qty.ToString("0.##"),
                Format(item.Batch.Mrp ?? 0m)
            ]).ToArray();
        return new ReportTable("Expiry report", ["Drug", "Batch", "Expiry", "Stock", "MRP"], rows);
    }

    private async Task<ReportTable> LowStockAsync(CancellationToken token)
    {
        var drugs = await context.Drugs.AsNoTracking().Where(item => item.IsActive && item.ReorderLevel > 0).ToListAsync(token);
        var ids = drugs.Select(item => item.Id).ToArray();
        var batches = await context.Batches.AsNoTracking().Where(item => ids.Contains(item.DrugId)).ToListAsync(token);
        var batchIds = batches.Select(item => item.Id).ToArray();
        var movements = await context.StockMovements.AsNoTracking().Where(item => batchIds.Contains(item.BatchId))
            .GroupBy(item => item.BatchId).Select(group => new { Id = group.Key, Qty = group.Sum(item => item.QuantityChange) })
            .ToDictionaryAsync(item => item.Id, item => item.Qty, token);
        var rows = drugs.Select(drug =>
            {
                var current = batches.Where(batch => batch.DrugId == drug.Id).Sum(batch => movements.GetValueOrDefault(batch.Id));
                return (Drug: drug, Current: current);
            })
            .Where(item => item.Current < item.Drug.ReorderLevel)
            .OrderBy(item => item.Current / item.Drug.ReorderLevel)
            .Select(item => (IReadOnlyList<string>)[
                item.Drug.Name,
                item.Current.ToString("0.##"),
                item.Drug.ReorderLevel.ToString("0.##"),
                Math.Max(0m, item.Drug.ReorderLevel - item.Current).ToString("0.##")
            ]).ToArray();
        return new ReportTable("Low-stock report", ["Drug", "On hand", "Reorder level", "Suggested order"], rows);
    }

    private async Task<ReportTable> SupplierWiseAsync(DateOnly from, DateOnly to, CancellationToken token)
    {
        var invoices = await context.PurchaseInvoices.AsNoTracking()
            .Where(item => item.Status == "Posted" && item.InvoiceDate >= from && item.InvoiceDate <= to)
            .ToListAsync(token);
        var suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
        var rows = invoices.GroupBy(item => item.SupplierId)
            .Select(group => (IReadOnlyList<string>)[
                suppliers.GetValueOrDefault(group.Key, "Unknown"),
                group.Count().ToString(),
                Format(group.Sum(item => item.TotalAmount))
            ]).OrderByDescending(row => decimal.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        return new ReportTable("Supplier-wise purchases", ["Supplier", "Invoices", "Purchase total"], rows);
    }

    private async Task<decimal> GetWholesaleOutstandingAsync(DateOnly asOf, CancellationToken token)
    {
        var invoices = await context.WholesaleInvoices.AsNoTracking()
            .Where(item => item.Status == "Posted")
            .ToListAsync(token);
        var receipts = await context.Receipts.AsNoTracking()
            .Where(item => item.WholesaleInvoiceId.HasValue)
            .GroupBy(item => item.WholesaleInvoiceId!.Value)
            .Select(group => new { Id = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToDictionaryAsync(item => item.Id, item => item.Amount, token);
        var credits = await context.CustomerLedgerEntries.AsNoTracking()
            .Where(item => item.EntryType.Contains("CreditNote"))
            .ToListAsync(token);
        return invoices.Where(item => DateOnly.FromDateTime(item.InvoiceAtUtc.ToLocalTime()) <= asOf)
            .Sum(invoice =>
            {
                var credited = credits.Where(entry => entry.CustomerId == invoice.CustomerId &&
                    entry.Notes?.Contains(invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase) == true)
                    .Sum(entry => entry.Credit);
                return Math.Max(0m, invoice.TotalAmount - Math.Max(invoice.PaidAmount, receipts.GetValueOrDefault(invoice.Id)) - credited);
            });
    }

    private static (DateTime StartUtc, DateTime EndUtc) GetUtcBounds(DateOnly from, DateOnly to) =>
        (from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime(),
         to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime());

    private static IReadOnlyList<string> Row(string date, string invoice, string party, decimal total, decimal paid, decimal tax) =>
        [date, invoice, party, Format(total), Format(paid), Format(tax)];

    private static string Format(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
