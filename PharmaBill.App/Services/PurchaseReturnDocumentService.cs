using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PharmaBill.App.Services;

/// <summary>A saved purchase return note (debit note to the supplier) and the details needed to send it on.</summary>
public sealed record PurchaseReturnNote(string Path, string ReturnNo, string SupplierName, string? SupplierPhone, string? SupplierEmail, decimal Total);

/// <summary>Builds the purchase return note PDF and sends it to the printer, a folder, WhatsApp or email.</summary>
public sealed class PurchaseReturnDocumentService(IServiceScopeFactory scopeFactory)
{
	private static readonly CultureInfo IndianCulture = CultureInfo.GetCultureInfo("en-IN");

	public async Task<PurchaseReturnNote> ExportAsync(Guid purchaseReturnId, CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		PurchaseReturn purchaseReturn = await context.PurchaseReturns.AsNoTracking().SingleOrDefaultAsync(item => item.Id == purchaseReturnId, cancellationToken)
			?? throw new InvalidOperationException("Purchase return was not found.");
		Supplier supplier = await context.Suppliers.AsNoTracking().SingleAsync(item => item.Id == purchaseReturn.SupplierId, cancellationToken);
		PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		var lines = await (from line in context.PurchaseReturnItems.AsNoTracking()
			join drug in context.Drugs.AsNoTracking() on line.DrugId equals drug.Id
			join batch in context.Batches.AsNoTracking() on line.BatchId equals batch.Id
			where line.PurchaseReturnId == purchaseReturn.Id
			select new { drug.Name, batch.BatchNo, batch.ExpiryDate, line.Quantity, line.UnitPrice, line.LineTotal })
			.ToListAsync(cancellationToken);

		string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaBill", "purchase-returns");
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, $"return-{purchaseReturn.Id:N}.pdf");

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

					column.Item().Text("GSTIN: " + (profile.Gstin ?? "Not provided"));
					column.Item().Text("PURCHASE RETURN NOTE (DEBIT NOTE)").FontSize(15f).Bold().AlignCenter();
				});
				page.Content().Column(column =>
				{
					column.Spacing(8f);
					column.Item().Row(row =>
					{
						row.RelativeItem().Column(details =>
						{
							details.Item().Text("Return no: " + purchaseReturn.ReturnNo).Bold();
							details.Item().Text($"Date: {purchaseReturn.ReturnDate:dd-MMM-yyyy}");
							details.Item().Text("Reason: " + (string.IsNullOrWhiteSpace(purchaseReturn.Reason) ? "—" : purchaseReturn.Reason));
						});
						row.RelativeItem().Column(details =>
						{
							details.Item().Text("To (supplier): " + supplier.Name).Bold();
							details.Item().Text(supplier.Address ?? string.Empty);
							details.Item().Text("Supplier GSTIN: " + (supplier.Gstin ?? "Not recorded"));
							details.Item().Text("Phone: " + (supplier.Phone ?? "—"));
						});
					});
					column.Item().Table(table =>
					{
						table.ColumnsDefinition(columns =>
						{
							columns.ConstantColumn(28f);
							columns.RelativeColumn(3f);
							columns.RelativeColumn(1.4f);
							columns.ConstantColumn(60f);
							columns.ConstantColumn(50f);
							columns.ConstantColumn(60f);
							columns.ConstantColumn(70f);
						});
						table.Header(header =>
						{
							foreach (string title in new[] { "#", "Medicine", "Batch", "Expiry", "Qty", "Rate", "Amount" })
							{
								header.Cell().Background(Colors.Grey.Lighten3).Padding(4f).Text(title).Bold();
							}
						});
						int index = 0;
						foreach (var line in lines)
						{
							index++;
							table.Cell().Padding(4f).Text(index.ToString(CultureInfo.InvariantCulture));
							table.Cell().Padding(4f).Text(line.Name);
							table.Cell().Padding(4f).Text(line.BatchNo);
							table.Cell().Padding(4f).Text($"{line.ExpiryDate:MM/yyyy}");
							table.Cell().Padding(4f).Text(line.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
							table.Cell().Padding(4f).Text(line.UnitPrice.ToString("N2", IndianCulture));
							table.Cell().Padding(4f).Text(line.LineTotal.ToString("N2", IndianCulture));
						}
					});
					column.Item().AlignRight().Text("Total credit due: ₹" + purchaseReturn.TotalAmount.ToString("N2", IndianCulture)).FontSize(12f).Bold();
					column.Item().PaddingTop(30f).Row(row =>
					{
						row.RelativeItem().Text("Supplier acknowledgement");
						row.RelativeItem().AlignRight().Text("For " + profile.Name);
					});
				});
			});
		}).GeneratePdf(path);

		return new PurchaseReturnNote(path, purchaseReturn.ReturnNo, supplier.Name, supplier.Phone, supplier.Email, purchaseReturn.TotalAmount);
	}

	/// <summary>Opens the PDF with its default print action.</summary>
	public static void Print(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
		{
			throw new InvalidOperationException("Windows could not print the return note. Save it as PDF and print from there. " + ex.Message, ex);
		}
	}

	/// <summary>Opens Explorer with the saved PDF selected, ready to attach.</summary>
	public static void ShowInExplorer(string path)
	{
		Process.Start("explorer.exe", $"/select,\"{path}\"");
	}

	/// <summary>Opens a WhatsApp chat with the supplier. The PDF cannot be attached through the link, so attach it from Explorer.</summary>
	public static void OpenWhatsApp(string? phone, string message)
	{
		string digits = RetailBillPdfService.NormalizeWhatsAppPhone(phone);
		if (digits.Length == 0)
		{
			throw new InvalidOperationException("Add a phone number for this supplier (Suppliers) to send the return note on WhatsApp.");
		}

		string url = "https://wa.me/" + digits + "?text=" + Uri.EscapeDataString(message);
		Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
	}

	/// <summary>Opens a mail draft to the supplier. Attach the saved PDF from Explorer.</summary>
	public static void OpenEmail(string? recipient, string subject, string body)
	{
		if (string.IsNullOrWhiteSpace(recipient))
		{
			throw new InvalidOperationException("Add an email address for this supplier (Suppliers) to send the return note by email.");
		}

		string url = $"mailto:{Uri.EscapeDataString(recipient.Trim())}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
		Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
	}
}
