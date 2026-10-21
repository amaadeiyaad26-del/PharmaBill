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

/// <summary>Builds the quotation PDF and opens it for printing or sharing. The quote is an estimate, not a tax invoice.</summary>
public sealed class QuotationDocumentService(IServiceScopeFactory scopeFactory)
{
	private static readonly CultureInfo IndianCulture = CultureInfo.GetCultureInfo("en-IN");

	public async Task<string> ExportAsync(Guid quotationId, CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		Quotation quotation = await context.Quotations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == quotationId, cancellationToken)
			?? throw new InvalidOperationException("Quotation was not found.");
		PharmacyProfile profile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		var lines = await context.QuotationItems.AsNoTracking()
			.Where(item => item.QuotationId == quotationId)
			.Select(item => new { item.DrugName, item.Quantity, item.FreeQuantity, item.UnitPrice, item.DiscountAmount, item.LineTotal })
			.ToListAsync(cancellationToken);

		string folder = Path.Combine(AppDataPaths.RootDirectory, "quotations");
		Directory.CreateDirectory(folder);
		string path = Path.Combine(folder, $"quotation-{quotation.Id:N}.pdf");

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
					column.Item().Text("QUOTATION").FontSize(15f).Bold().AlignCenter();
				});
				page.Content().Column(column =>
				{
					column.Spacing(8f);
					column.Item().Row(row =>
					{
						row.RelativeItem().Column(details =>
						{
							details.Item().Text("Quotation no: " + quotation.QuoteNo).Bold();
							details.Item().Text($"Date: {quotation.QuoteDate:dd-MMM-yyyy}");
							details.Item().Text("Valid until: " + (quotation.ValidUntil.HasValue ? quotation.ValidUntil.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) : "—"));
						});
						row.RelativeItem().Column(details =>
						{
							details.Item().Text("To: " + quotation.PartyName).Bold();
							details.Item().Text("Phone: " + (quotation.PartyPhone ?? "—"));
							details.Item().Text("Type: " + quotation.Mode);
						});
					});
					column.Item().Table(table =>
					{
						table.ColumnsDefinition(columns =>
						{
							columns.ConstantColumn(28f);
							columns.RelativeColumn(3.5f);
							columns.ConstantColumn(55f);
							columns.ConstantColumn(60f);
							columns.ConstantColumn(60f);
							columns.ConstantColumn(60f);
							columns.ConstantColumn(70f);
						});
						table.Header(header =>
						{
							foreach (string title in new[] { "#", "Medicine", "Qty", "Free", "Rate", "Discount", "Amount" })
							{
								header.Cell().Background(Colors.Grey.Lighten3).Padding(4f).Text(title).Bold();
							}
						});
						int index = 0;
						foreach (var line in lines)
						{
							index++;
							table.Cell().Padding(4f).Text(index.ToString(CultureInfo.InvariantCulture));
							table.Cell().Padding(4f).Text(line.DrugName);
							table.Cell().Padding(4f).Text(line.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
							table.Cell().Padding(4f).Text(line.FreeQuantity.ToString("0.##", CultureInfo.InvariantCulture));
							table.Cell().Padding(4f).Text(line.UnitPrice.ToString("N2", IndianCulture));
							table.Cell().Padding(4f).Text(line.DiscountAmount.ToString("N2", IndianCulture));
							table.Cell().Padding(4f).Text(line.LineTotal.ToString("N2", IndianCulture));
						}
					});
					column.Item().AlignRight().Text("Estimated total: ₹" + quotation.EstimatedTotal.ToString("N2", IndianCulture)).FontSize(12f).Bold();
					column.Item().Text("This is an estimate. GST and final prices are calculated when the bill is issued. Stock is not reserved until then.").FontSize(8f);
					if (!string.IsNullOrWhiteSpace(quotation.Notes))
					{
						column.Item().Text("Notes: " + quotation.Notes).FontSize(8f);
					}
				});
			});
		}).GeneratePdf(path);

		return path;
	}

	/// <summary>Opens the PDF with its default print action.</summary>
	public static void Print(string path)
	{
		Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "print" });
	}

	/// <summary>Opens Explorer with the saved PDF selected.</summary>
	public static void ShowInExplorer(string path)
	{
		Process.Start("explorer.exe", $"/select,\"{path}\"");
	}
}
