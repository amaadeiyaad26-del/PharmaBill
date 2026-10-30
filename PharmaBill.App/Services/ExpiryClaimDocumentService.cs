using System.Globalization;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace PharmaBill.App.Services;

/// <summary>
/// The expiry return claim statement the shop sends with the goods: every line, totals, GST, the policy applied, and
/// space for the distributor to sign. Everything printed is as recorded on the claim when it was saved.
/// </summary>
public sealed class ExpiryClaimDocumentService(IServiceScopeFactory scopeFactory)
{
	private static readonly CultureInfo Indian = CultureInfo.GetCultureInfo("en-IN");

	/// <summary>Writes the PDF to <paramref name="destination"/>, or to the data folder when null; returns where.</summary>
	public async Task<string> ExportPdfAsync(Guid claimId, string? destination = null, CancellationToken cancellationToken = default)
	{
		ExpiryClaim claim;
		IReadOnlyList<ExpiryClaimLine> lines;
		IReadOnlyList<ExpiryClaimCredit> credits;
		using (IServiceScope scope = scopeFactory.CreateScope())
		{
			(claim, lines, credits) = await scope.ServiceProvider.GetRequiredService<ExpiryReturnService>().GetClaimAsync(claimId, cancellationToken);
		}
		string path = destination ?? Path.Combine(AppDataPaths.RootDirectory, "expiry-claims", $"claim-{claim.Id:N}.pdf");
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		await Task.Run(() => Build(claim, lines, credits).GeneratePdf(path), cancellationToken);
		return path;
	}

	internal static Document Build(ExpiryClaim claim, IReadOnlyList<ExpiryClaimLine> lines, IReadOnlyList<ExpiryClaimCredit> credits)
	{
		string M(decimal value) => value.ToString("N2", Indian);
		return Document.Create(container => container.Page(page =>
		{
			page.Size(PageSizes.A4.Landscape());
			page.Margin(24f);
			page.DefaultTextStyle(style => style.FontFamily(PdfFonts.Family).FontSize(8.5f));
			page.Header().Column(column =>
			{
				column.Item().Row(row =>
				{
					row.RelativeItem().Column(from =>
					{
						from.Item().Text(claim.PharmacyName).FontSize(16f).Bold();
						if (claim.PharmacyAddress.Length > 0)
						{
							from.Item().Text(claim.PharmacyAddress);
						}
						from.Item().Text("GSTIN: " + (claim.PharmacyGstin.Length > 0 ? claim.PharmacyGstin : "Not registered") + (claim.PharmacyPhone.Length > 0 ? "   Phone: " + claim.PharmacyPhone : ""));
						if (claim.PharmacyDrugLicence.Length > 0)
						{
							from.Item().Text("Drug licence: " + claim.PharmacyDrugLicence);
						}
					});
					row.RelativeItem().AlignRight().Column(to =>
					{
						to.Item().AlignRight().Text("EXPIRY RETURN CLAIM / DEBIT NOTE").FontSize(14f).Bold();
						to.Item().AlignRight().Text("Claim no: " + claim.ClaimNo).Bold();
						to.Item().AlignRight().Text($"Claim date: {claim.ClaimDate:dd-MMM-yyyy}");
						if (claim.SentDate is DateOnly sent)
						{
							to.Item().AlignRight().Text($"Sent: {sent:dd-MMM-yyyy} via {claim.SentVia}" + (string.IsNullOrWhiteSpace(claim.SentTo) ? "" : " to " + claim.SentTo));
						}
					});
				});
				column.Item().PaddingTop(6f).Border(0.6f).BorderColor(Colors.Grey.Lighten1).Padding(6f).Row(row =>
				{
					row.RelativeItem().Column(supplier =>
					{
						supplier.Item().Text("To (distributor): " + claim.SupplierName).Bold();
						if (claim.SupplierAddress.Length > 0)
						{
							supplier.Item().Text(claim.SupplierAddress);
						}
						supplier.Item().Text("GSTIN: " + (claim.SupplierGstin.Length > 0 ? claim.SupplierGstin : "Not recorded") + "   State code: " + claim.SupplierStateCode
							+ (claim.SupplierDrugLicence.Length > 0 ? "   DL: " + claim.SupplierDrugLicence : ""));
					});
					row.RelativeItem().Column(policy =>
					{
						policy.Item().Text("Return policy applied").Bold();
						policy.Item().Text(new ExpiryReturnWindow(claim.PolicyDaysBeforeExpiry, claim.PolicyDaysAfterExpiry).Describe() + ".");
						if (claim.PolicyNote.Length > 0)
						{
							policy.Item().Text("Distributor's stated policy: " + claim.PolicyNote);
						}
						policy.Item().Text(claim.IsInterState ? "Inter-state: IGST" : "Intra-state: CGST + SGST");
					});
				});
			});
			page.Content().PaddingTop(8f).Column(column =>
			{
				column.Spacing(6f);
				column.Item().Table(table =>
				{
					table.ColumnsDefinition(columns =>
					{
						columns.ConstantColumn(20f);
						columns.RelativeColumn(3.2f);
						columns.RelativeColumn(1.1f);
						columns.ConstantColumn(54f);
						columns.RelativeColumn(1.2f);
						columns.ConstantColumn(44f);
						columns.ConstantColumn(34f);
						columns.ConstantColumn(50f);
						columns.ConstantColumn(60f);
						columns.ConstantColumn(34f);
						columns.ConstantColumn(50f);
						columns.ConstantColumn(50f);
						columns.ConstantColumn(64f);
					});
					string[] titles = claim.IsInterState
						? ["#", "Product", "Pack", "HSN", "Batch", "Expiry", "Qty", "Rate", "Taxable", "GST %", "IGST", "", "Value"]
						: ["#", "Product", "Pack", "HSN", "Batch", "Expiry", "Qty", "Rate", "Taxable", "GST %", "CGST", "SGST", "Value"];
					table.Header(header =>
					{
						foreach (string title in titles)
						{
							header.Cell().Background(Colors.Grey.Lighten3).Padding(3f).Text(title).Bold();
						}
					});
					int index = 0;
					foreach (ExpiryClaimLine line in lines)
					{
						index++;
						table.Cell().Padding(3f).Text(index.ToString(CultureInfo.InvariantCulture));
						table.Cell().Padding(3f).Text(line.ItemName);
						table.Cell().Padding(3f).Text(line.Pack);
						table.Cell().Padding(3f).Text(line.HsnCode);
						table.Cell().Padding(3f).Text(line.BatchNo);
						table.Cell().Padding(3f).Text(line.BatchExpiry?.ToString("MM/yyyy", CultureInfo.InvariantCulture) ?? "");
						table.Cell().Padding(3f).AlignRight().Text(line.Quantity.ToString("0.##", CultureInfo.InvariantCulture));
						table.Cell().Padding(3f).AlignRight().Text(M(line.PurchaseRate));
						table.Cell().Padding(3f).AlignRight().Text(M(line.TaxableValue));
						table.Cell().Padding(3f).AlignRight().Text(line.GstRate.ToString("0.##", CultureInfo.InvariantCulture));
						table.Cell().Padding(3f).AlignRight().Text(M(claim.IsInterState ? line.IgstAmount : line.CgstAmount));
						table.Cell().Padding(3f).AlignRight().Text(claim.IsInterState ? "" : M(line.SgstAmount));
						table.Cell().Padding(3f).AlignRight().Text(M(line.LineValue));
					}
					table.Cell().ColumnSpan(8).Padding(3f).AlignRight().Text("Total").Bold();
					table.Cell().Padding(3f).AlignRight().Text(M(claim.TaxableValue)).Bold();
					table.Cell().Padding(3f).Text("");
					table.Cell().Padding(3f).AlignRight().Text(M(claim.IsInterState ? claim.IgstAmount : claim.CgstAmount)).Bold();
					table.Cell().Padding(3f).AlignRight().Text(claim.IsInterState ? "" : M(claim.SgstAmount)).Bold();
					table.Cell().Padding(3f).AlignRight().Text(M(claim.TotalValue)).Bold();
				});
				column.Item().AlignRight().Text($"Amount claimed: ₹{M(claim.TotalValue)}").FontSize(12f).Bold();
				column.Item().Text("Goods returned for credit at the purchase rate shown. Please issue a credit note quoting claim no " + claim.ClaimNo + ".");
				if (credits.Count > 0)
				{
					column.Item().PaddingTop(4f).Text("Credit notes received").Bold();
					foreach (ExpiryClaimCredit credit in credits)
					{
						column.Item().Text($"{credit.CreditNoteNo} dated {credit.CreditNoteDate:dd-MMM-yyyy}: ₹{M(credit.Amount)} ({ExpiryReturnService.SettlementText(credit.Settlement)}); distributor's GST: taxable ₹{M(credit.TaxableValue)}, CGST ₹{M(credit.CgstAmount)}, SGST ₹{M(credit.SgstAmount)}, IGST ₹{M(credit.IgstAmount)}");
					}
				}
				column.Item().PaddingTop(28f).Row(row =>
				{
					row.RelativeItem().Border(0.6f).BorderColor(Colors.Grey.Lighten1).Padding(8f).Column(sign =>
					{
						sign.Item().Text("Received the goods listed above (distributor)").Bold();
						sign.Item().PaddingTop(26f).Text("Name: ______________________________");
						sign.Item().PaddingTop(8f).Text("Signature & stamp: ______________________   Date: ____________");
					});
					row.ConstantItem(24f);
					row.RelativeItem().Border(0.6f).BorderColor(Colors.Grey.Lighten1).Padding(8f).Column(sign =>
					{
						sign.Item().Text("For " + claim.PharmacyName).Bold();
						sign.Item().PaddingTop(26f).Text("Prepared by: " + claim.CreatedByName);
						sign.Item().PaddingTop(8f).Text("Authorised signatory: ______________________");
					});
				});
			});
			page.Footer().AlignCenter().Text(text =>
			{
				text.Span(claim.ClaimNo + " · page ");
				text.CurrentPageNumber();
				text.Span(" of ");
				text.TotalPages();
			});
		}));
	}
}
