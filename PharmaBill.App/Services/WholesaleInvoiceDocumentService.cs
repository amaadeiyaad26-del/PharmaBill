using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core;
using PharmaBill.Data.Persistence;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaBill.App.Services;

public sealed class WholesaleInvoiceDocumentService(IServiceScopeFactory scopeFactory)
{
    public async Task<string> ExportAsync(
        Guid invoiceId,
        bool deliveryChallan,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var invoice = await context.WholesaleInvoices.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("Wholesale invoice was not found.");
        var profile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
        var customer = await context.Customers.AsNoTracking()
            .SingleAsync(item => item.Id == invoice.CustomerId, cancellationToken);
        var licences = await context.CustomerLicences.AsNoTracking()
            .Where(item => item.CustomerId == customer.Id)
            .OrderBy(item => item.LicenceType)
            .ThenBy(item => item.LicenceNumber)
            .ToListAsync(cancellationToken);
        var lines = await (from line in context.WholesaleInvoiceItems.AsNoTracking()
                           join drug in context.Drugs.AsNoTracking() on line.DrugId equals drug.Id
                           join batch in context.Batches.AsNoTracking() on line.BatchId equals batch.Id
                           where line.WholesaleInvoiceId == invoice.Id
                           select new
                           {
                               drug.Name,
                               drug.HsnCode,
                               line.Quantity,
                               line.FreeQuantity,
                               line.UnitPrice,
                               line.TaxRate,
                               line.TaxableAmount,
                               line.CgstAmount,
                               line.SgstAmount,
                               line.IgstAmount,
                               line.LineTotal,
                               batch.BatchNo,
                               batch.ExpiryDate
                           }).ToListAsync(cancellationToken);

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill",
            "wholesale-documents");
        Directory.CreateDirectory(directory);
        var kind = deliveryChallan ? "delivery-challan" : "tax-invoice";
        var destination = Path.Combine(directory, $"{invoice.Id:N}-{kind}.pdf");
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(style => style.FontFamily(PdfFonts.Family).FontSize(9));
                page.Header().Column(column =>
                {
                    column.Item().Text(profile.Name).FontSize(18).Bold();
                    if (!string.IsNullOrWhiteSpace(profile.Address))
                    {
                        column.Item().Text(profile.Address);
                    }
                    column.Item().Text(deliveryChallan ? "DELIVERY CHALLAN" : "TAX INVOICE")
                        .FontSize(15).Bold().AlignCenter();
                });
                page.Content().Column(column =>
                {
                    column.Spacing(8);
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(details =>
                        {
                            details.Item().Text($"Invoice no: {invoice.InvoiceNo}").Bold();
                            details.Item().Text($"Date: {invoice.InvoiceAtUtc.ToLocalTime():dd-MMM-yyyy}");
                            details.Item().Text($"Buyer: {customer.Name}").Bold();
                            details.Item().Text(customer.Address ?? string.Empty);
                            details.Item().Text($"GSTIN: {customer.Gstin ?? "Not provided"}");
                        });
                        row.RelativeItem().Column(details =>
                        {
                            details.Item().Text($"Buyer state: {customer.State ?? "Not recorded"}");
                            details.Item().Text($"Transport: {invoice.TransportDetails ?? "—"}");
                            details.Item().Text($"Vehicle: {invoice.VehicleNumber ?? "—"}");
                            details.Item().Text($"E-way bill no: {invoice.EWayBillNumber ?? "Not configured"}");
                            details.Item().Text($"IRN: {invoice.Irn ?? "Not configured"}");
                        });
                    });
                    column.Item().Text($"Buyer drug licences: {string.Join("; ", licences.Select(item =>
                        $"{item.LicenceType} {item.LicenceNumber} (expires {item.ExpiresOn?.ToString("dd-MMM-yyyy") ?? "not recorded"})"))}");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1.1f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(1.4f);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Medicine / HSN");
                            header.Cell().Element(HeaderCell).Text("Batch");
                            header.Cell().Element(HeaderCell).Text("Expiry");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Qty + free");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Rate");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                        });
                        foreach (var line in lines)
                        {
                            table.Cell().Element(BodyCell).Text($"{line.Name} / {line.HsnCode ?? "—"}");
                            table.Cell().Element(BodyCell).Text(line.BatchNo);
                            table.Cell().Element(BodyCell).Text(line.ExpiryDate?.ToString("MM/yyyy") ?? "—");
                            table.Cell().Element(BodyCell).AlignRight().Text($"{line.Quantity:0.##} + {line.FreeQuantity:0.##}");
                            table.Cell().Element(BodyCell).AlignRight().Text($"{MoneyFormat.Rupees(line.UnitPrice)}");
                            table.Cell().Element(BodyCell).AlignRight().Text($"{MoneyFormat.Rupees(line.LineTotal)}");
                        }
                    });
                    column.Item().AlignRight().Text($"Taxable: {MoneyFormat.Rupees(invoice.Subtotal)}");
                    column.Item().AlignRight().Text($"CGST: {MoneyFormat.Rupees(invoice.CgstAmount)}    SGST: {MoneyFormat.Rupees(invoice.SgstAmount)}    IGST: {MoneyFormat.Rupees(invoice.IgstAmount)}");
                    column.Item().AlignRight().Text($"Round-off: {MoneyFormat.Rupees(invoice.RoundOff)}");
                    column.Item().AlignRight().Text($"Grand total: {MoneyFormat.Rupees(invoice.TotalAmount)}").FontSize(13).Bold();
                    column.Item().Text($"Amount in words: {IndianNumberWords.Convert(invoice.TotalAmount)}").Bold();
                    if (!deliveryChallan)
                    {
                        column.Item().Text($"Bank: {profile.BankName ?? "Not configured"}; account: {profile.BankAccountName ?? "Not configured"}; IFSC: {profile.BankIfsc ?? "Not configured"}");
                        column.Item().Text("Terms: As per the seller's recorded terms of sale.");
                    }
                });
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text($"Competent person: {profile.CompetentPersonName ?? "Not recorded"}");
                    row.RelativeItem().AlignRight().Text("Authorised signatory ____________________");
                });
            });
        }).GeneratePdf(destination);
        return destination;
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten2).Padding(4).BorderBottom(1).BorderColor(Colors.Grey.Medium);

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4);
}
