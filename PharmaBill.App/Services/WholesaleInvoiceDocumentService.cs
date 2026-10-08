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
		Branch? branch = invoice.BranchId.HasValue
			? await context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == invoice.BranchId.Value, cancellationToken)
			: await context.Branches.AsNoTracking().FirstOrDefaultAsync(b => b.IsActive, cancellationToken);
		List<LicenceRecord> sellerLicences = await context.LicenceRecords.AsNoTracking()
			.OrderBy(item => item.LicenceType)
			.ThenBy(item => item.LicenceNumber)
			.ToListAsync(cancellationToken);
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
				line.LineTotal, batch.BatchNo, batch.ExpiryDate, batch.Mrp
			}).ToListAsync(cancellationToken);

		string sellerDl = string.Join("; ", sellerLicences.Select(item => $"{item.LicenceType} {item.LicenceNumber}".Trim()).Where(s => !string.IsNullOrWhiteSpace(s)));
		if (string.IsNullOrWhiteSpace(sellerDl) && !string.IsNullOrWhiteSpace(branch?.DrugLicenseNo))
		{
			sellerDl = branch.DrugLicenseNo.Trim();
		}

		string sellerGstin = !string.IsNullOrWhiteSpace(branch?.Gstin) ? branch.Gstin.Trim() : (profile.Gstin ?? "Not provided");
		string buyerDl = licences.Count == 0
			? "Not recorded"
			: string.Join("; ", licences.Select(item => $"{item.LicenceType} {item.LicenceNumber} (expires {item.ExpiresOn?.ToString("dd-MMM-yyyy") ?? "not recorded"})"));

		var hsnGroups = lines
			.GroupBy(item => string.IsNullOrWhiteSpace(item.HsnCode) ? "—" : item.HsnCode!)
			.Select(g => new
			{
				Hsn = g.Key,
				Taxable = g.Sum(x => x.TaxableAmount),
				Cgst = g.Sum(x => x.CgstAmount),
				Sgst = g.Sum(x => x.SgstAmount),
				Igst = g.Sum(x => x.IgstAmount),
				Rate = g.Select(x => x.TaxRate).FirstOrDefault()
			})
			.OrderBy(g => g.Hsn)
			.ToList();

		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "wholesale-documents");
		Directory.CreateDirectory(text);
		string value = deliveryChallan ? "delivery-challan" : "tax-invoice";
		string text2 = Path.Combine(text, $"{invoice.Id:N}-{value}.pdf");
		Document.Create(container =>
		{
			container.Page(page =>
			{
				page.Size(PageSizes.A4);
				page.Margin(28f);
				page.DefaultTextStyle(style => style.FontFamily(PdfFonts.Family).FontSize(9f));
				PdfBrandWatermark.Apply(page);
				page.Header().Column(column =>
				{
					column.Item().Text(profile.Name).FontSize(18f).Bold();
					if (!string.IsNullOrWhiteSpace(profile.Address))
					{
						column.Item().Text(profile.Address);
					}
					column.Item().Text("Seller GSTIN: " + sellerGstin);
					column.Item().Text("Seller DL No: " + (string.IsNullOrWhiteSpace(sellerDl) ? "Not recorded" : sellerDl));
					column.Item().Text(deliveryChallan ? "DELIVERY CHALLAN" : "TAX INVOICE (B2B)").FontSize(15f).Bold().AlignCenter();
				});
				page.Content().Column(column =>
				{
					column.Spacing(8f);
					column.Item().Row(row =>
					{
						row.RelativeItem().Column(details =>
						{
							details.Item().Text("Invoice no: " + invoice.InvoiceNo).Bold();
							details.Item().Text($"Date: {invoice.InvoiceAtUtc.ToLocalTime():dd-MMM-yyyy}");
							details.Item().Text("Buyer: " + customer.Name).Bold();
							details.Item().Text(customer.Address ?? string.Empty);
							details.Item().Text("Buyer GSTIN: " + (customer.Gstin ?? "Not provided / unregistered"));
							details.Item().Text("Buyer DL No: " + buyerDl);
							details.Item().Text("Buyer phone: " + (customer.Phone ?? "—"));
						});
						row.RelativeItem().Column(details =>
						{
							details.Item().Text("Buyer state: " + (customer.State ?? "Not recorded"));
							details.Item().Text("Transport: " + (invoice.TransportDetails ?? "—"));
							details.Item().Text("Vehicle: " + (invoice.VehicleNumber ?? "—"));
							details.Item().Text("E-way bill no: " + (invoice.EWayBillNumber ?? "Not configured"));
							details.Item().Text("IRN: " + (invoice.Irn ?? "Not configured"));
						});
					});
					column.Item().Table(table =>
					{
						table.ColumnsDefinition(columns =>
						{
							columns.RelativeColumn(3.6f);
							columns.RelativeColumn(1.1f);
							columns.RelativeColumn(1.1f);
							columns.RelativeColumn(1.1f);
							columns.RelativeColumn(1.1f);
							columns.RelativeColumn(1.2f);
							columns.RelativeColumn(1.3f);
						});
						table.Header(header =>
						{
							header.Cell().Element(HeaderCell).Text("Medicine / HSN");
							header.Cell().Element(HeaderCell).Text("Batch");
							header.Cell().Element(HeaderCell).Text("Expiry");
							header.Cell().Element(HeaderCell).AlignRight().Text("Qty + free");
							header.Cell().Element(HeaderCell).AlignRight().Text("MRP");
							header.Cell().Element(HeaderCell).AlignRight().Text("Rate/PTR");
							header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
						});
						foreach (var item in lines)
						{
							table.Cell().Element(BodyCell).Text(item.Name + " / " + (item.HsnCode ?? "—"));
							table.Cell().Element(BodyCell).Text(item.BatchNo);
							table.Cell().Element(BodyCell).Text(item.ExpiryDate?.ToString("MM/yyyy") ?? "—");
							table.Cell().Element(BodyCell).AlignRight().Text($"{item.Quantity:0.##} + {item.FreeQuantity:0.##}");
							table.Cell().Element(BodyCell).AlignRight().Text(MoneyFormat.Rupees(item.Mrp.GetValueOrDefault()) ?? "—");
							table.Cell().Element(BodyCell).AlignRight().Text(MoneyFormat.Rupees(item.UnitPrice) ?? "");
							table.Cell().Element(BodyCell).AlignRight().Text(MoneyFormat.Rupees(item.LineTotal) ?? "");
						}
					});

					if (!deliveryChallan && hsnGroups.Count > 0)
					{
						column.Item().Text("HSN-wise tax breakdown").SemiBold();
						column.Item().Table(table =>
						{
							table.ColumnsDefinition(columns =>
							{
								columns.RelativeColumn(1.4f);
								columns.RelativeColumn(1.2f);
								columns.RelativeColumn(1.6f);
								columns.RelativeColumn(1.4f);
								columns.RelativeColumn(1.4f);
								columns.RelativeColumn(1.4f);
							});
							table.Header(header =>
							{
								header.Cell().Element(HeaderCell).Text("HSN");
								header.Cell().Element(HeaderCell).AlignRight().Text("GST %");
								header.Cell().Element(HeaderCell).AlignRight().Text("Taxable");
								header.Cell().Element(HeaderCell).AlignRight().Text("CGST");
								header.Cell().Element(HeaderCell).AlignRight().Text("SGST");
								header.Cell().Element(HeaderCell).AlignRight().Text("IGST");
							});
							foreach (var group in hsnGroups)
							{
								table.Cell().Element(BodyCell).Text(group.Hsn);
								table.Cell().Element(BodyCell).AlignRight().Text($"{group.Rate:0.##}%");
								table.Cell().Element(BodyCell).AlignRight().Text(MoneyFormat.Rupees(group.Taxable) ?? "");
								table.Cell().Element(BodyCell).AlignRight().Text(MoneyFormat.Rupees(group.Cgst) ?? "");
								table.Cell().Element(BodyCell).AlignRight().Text(MoneyFormat.Rupees(group.Sgst) ?? "");
								table.Cell().Element(BodyCell).AlignRight().Text(MoneyFormat.Rupees(group.Igst) ?? "");
							}
						});
					}

					column.Item().AlignRight().Text("Taxable: " + MoneyFormat.Rupees(invoice.Subtotal));
					column.Item().AlignRight().Text($"CGST: {MoneyFormat.Rupees(invoice.CgstAmount)}    SGST: {MoneyFormat.Rupees(invoice.SgstAmount)}    IGST: {MoneyFormat.Rupees(invoice.IgstAmount)}");
					column.Item().AlignRight().Text("Round-off: " + MoneyFormat.Rupees(invoice.RoundOff));
					column.Item().AlignRight().Text("Grand total: " + MoneyFormat.Rupees(invoice.TotalAmount)).FontSize(13f).Bold();
					column.Item().Text("Amount in words: " + IndianNumberWords.Convert(invoice.TotalAmount)).Bold();
					if (!deliveryChallan)
					{
						column.Item().Text($"Bank: {profile.BankName ?? "Not configured"}; account: {profile.BankAccountName ?? "Not configured"}; IFSC: {profile.BankIfsc ?? "Not configured"}");
						column.Item().Text("Terms: As per the seller's recorded terms of sale. This is a B2B tax invoice — not a patient cash memo.");
					}
				});
				page.Footer().Row(row =>
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
		return container.DefaultTextStyle(style => style.SemiBold()).PaddingVertical(3f).BorderBottom(1f).BorderColor(Colors.Grey.Medium);
	}

	private static IContainer BodyCell(IContainer container)
	{
		return container.PaddingVertical(2f);
	}
}
