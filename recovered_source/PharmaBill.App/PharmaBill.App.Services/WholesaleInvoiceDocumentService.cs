using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaBill.App.Services;

public sealed class WholesaleInvoiceDocumentService(IServiceScopeFactory scopeFactory)
{
	public async Task<string> ExportAsync(Guid invoiceId, bool deliveryChallan, CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		WholesaleInvoice invoice = (await context.WholesaleInvoices.AsNoTracking().SingleOrDefaultAsync((WholesaleInvoice item) => item.Id == invoiceId, cancellationToken)) ?? throw new InvalidOperationException("Wholesale invoice was not found.");
		PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		Customer customer = await context.Customers.AsNoTracking().SingleAsync((Customer item) => item.Id == invoice.CustomerId, cancellationToken);
		List<CustomerLicence> licences = await (from item in context.CustomerLicences.AsNoTracking()
			where item.CustomerId == customer.Id
			orderby item.LicenceType, item.LicenceNumber
			select item).ToListAsync(cancellationToken);
		var lines = await (from line in context.WholesaleInvoiceItems.AsNoTracking()
			join drug in context.Drugs.AsNoTracking() on line.DrugId equals drug.Id
			join batch in context.Batches.AsNoTracking() on line.BatchId equals batch.Id
			where line.WholesaleInvoiceId == invoice.Id
			select new
			{
				drug.Name, drug.HsnCode, line.Quantity, line.FreeQuantity, line.UnitPrice, line.TaxRate, line.TaxableAmount, line.CgstAmount, line.SgstAmount, line.IgstAmount,
				line.LineTotal, batch.BatchNo, batch.ExpiryDate
			}).ToListAsync(cancellationToken);
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "wholesale-documents");
		Directory.CreateDirectory(text);
		string value = (deliveryChallan ? "delivery-challan" : "tax-invoice");
		string text2 = Path.Combine(text, $"{invoice.Id:N}-{value}.pdf");
		Document.Create((IDocumentContainer container) =>
		{
			container.Page((PageDescriptor page) =>
			{
				page.Size(PageSizes.A4);
				page.Margin(28f);
				page.DefaultTextStyle((TextStyle style) => style.FontFamily(PdfFonts.Family).FontSize(9f));
				PdfBrandWatermark.Apply(page);
				page.Header().Column((ColumnDescriptor column) =>
				{
					column.Item().Text(profile.Name).FontSize(18f)
						.Bold();
					if (!string.IsNullOrWhiteSpace(profile.Address))
					{
						column.Item().Text(profile.Address);
					}
					column.Item().Text(deliveryChallan ? "DELIVERY CHALLAN" : "TAX INVOICE").FontSize(15f)
						.Bold()
						.AlignCenter();
				});
				page.Content().Column((ColumnDescriptor column) =>
				{
					column.Spacing(8f);
					column.Item().Row((RowDescriptor row) =>
					{
						row.RelativeItem().Column((ColumnDescriptor details) =>
						{
							details.Item().Text("Invoice no: " + invoice.InvoiceNo).Bold();
							details.Item().Text($"Date: {invoice.InvoiceAtUtc.ToLocalTime():dd-MMM-yyyy}");
							details.Item().Text("Buyer: " + customer.Name).Bold();
							details.Item().Text(customer.Address ?? string.Empty);
							details.Item().Text("GSTIN: " + (customer.Gstin ?? "Not provided"));
						});
						row.RelativeItem().Column((ColumnDescriptor details) =>
						{
							details.Item().Text("Buyer state: " + (customer.State ?? "Not recorded"));
							details.Item().Text("Transport: " + (invoice.TransportDetails ?? "—"));
							details.Item().Text("Vehicle: " + (invoice.VehicleNumber ?? "—"));
							details.Item().Text("E-way bill no: " + (invoice.EWayBillNumber ?? "Not configured"));
							details.Item().Text("IRN: " + (invoice.Irn ?? "Not configured"));
						});
					});
					column.Item().Text("Buyer drug licences: " + string.Join("; ", licences.Select((CustomerLicence item) => $"{item.LicenceType} {item.LicenceNumber} (expires {item.ExpiresOn?.ToString("dd-MMM-yyyy") ?? "not recorded"})")));
					column.Item().Table((TableDescriptor table) =>
					{
						table.ColumnsDefinition((TableColumnsDefinitionDescriptor columns) =>
						{
							columns.RelativeColumn(4f);
							columns.RelativeColumn(1.1f);
							columns.RelativeColumn(1.2f);
							columns.RelativeColumn(1.2f);
							columns.RelativeColumn(1.4f);
							columns.RelativeColumn(1.4f);
						});
						table.Header((TableCellDescriptor header) =>
						{
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 115).Text("Medicine / HSN");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 116).Text("Batch");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 117).Text("Expiry");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 118).AlignRight().Text("Qty + free");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 119).AlignRight().Text("Rate");
							((IContainer)header.Cell()).Element((Func<IContainer, IContainer>)HeaderCell, "HeaderCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 120).AlignRight().Text("Amount");
						});
						foreach (var item in lines)
						{
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 124).Text(item.Name + " / " + (item.HsnCode ?? "—"));
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 125).Text(item.BatchNo);
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 126).Text(item.ExpiryDate?.ToString("MM/yyyy") ?? "—");
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 127).AlignRight().Text($"{item.Quantity:0.##} + {item.FreeQuantity:0.##}");
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 128).AlignRight().Text(MoneyFormat.Rupees(item.UnitPrice) ?? "");
							((IContainer)table.Cell()).Element((Func<IContainer, IContainer>)BodyCell, "BodyCell", "ExportAsync", "C:\\Users\\newth\\PharmaBill\\PharmaBill.App\\Services\\WholesaleInvoiceDocumentService.cs", 129).AlignRight().Text(MoneyFormat.Rupees(item.LineTotal) ?? "");
						}
					});
					column.Item().AlignRight().Text("Taxable: " + MoneyFormat.Rupees(invoice.Subtotal));
					column.Item().AlignRight().Text($"CGST: {MoneyFormat.Rupees(invoice.CgstAmount)}    SGST: {MoneyFormat.Rupees(invoice.SgstAmount)}    IGST: {MoneyFormat.Rupees(invoice.IgstAmount)}");
					column.Item().AlignRight().Text("Round-off: " + MoneyFormat.Rupees(invoice.RoundOff));
					column.Item().AlignRight().Text("Grand total: " + MoneyFormat.Rupees(invoice.TotalAmount))
						.FontSize(13f)
						.Bold();
					column.Item().Text("Amount in words: " + IndianNumberWords.Convert(invoice.TotalAmount)).Bold();
					if (!deliveryChallan)
					{
						column.Item().Text($"Bank: {profile.BankName ?? "Not configured"}; account: {profile.BankAccountName ?? "Not configured"}; IFSC: {profile.BankIfsc ?? "Not configured"}");
						column.Item().Text("Terms: As per the seller's recorded terms of sale.");
					}
				});
				page.Footer().Row((RowDescriptor row) =>
				{
					row.RelativeItem().Text("Competent person: " + (profile.CompetentPersonName ?? "Not recorded"));
					row.RelativeItem().AlignRight().Text("Authorised signatory ____________________");
				});
			});
		}).GeneratePdf(text2);
		return text2;
	}

	private static IContainer HeaderCell(IContainer container)
	{
		return container.Background(Colors.Grey.Lighten2).Padding(4f).BorderBottom(1f)
			.BorderColor(Colors.Grey.Medium);
	}

	private static IContainer BodyCell(IContainer container)
	{
		return container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4f);
	}
}
