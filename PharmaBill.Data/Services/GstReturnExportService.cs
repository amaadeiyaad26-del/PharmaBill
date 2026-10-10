using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class GstReturnExportService(PharmaBillDbContext context)
{
	private sealed record GstLine(string Description, string? HsnCode, decimal Quantity, decimal Rate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, decimal LineTotal);

	private sealed record GstDocument(string InvoiceNo, DateOnly InvoiceDate, string? CounterpartyGstin, string PlaceOfSupply, decimal Taxable, decimal TaxAmount, decimal Cgst, decimal Sgst, decimal Igst, decimal InvoiceValue, bool IsCancelled, IReadOnlyList<GstLine> Lines);

	public const decimal B2clThresholdRupees = 250000m;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNamingPolicy = null
	};

	public async Task<GstTaxAuditResult> ValidatePeriodAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePeriod(from, to);
		List<GstTaxAuditIssue> issues = new List<GstTaxAuditIssue>();
		foreach (GstDocument item in await LoadDocumentsAsync(from, to, includeCancelled: false, cancellationToken))
		{
			if (!string.IsNullOrWhiteSpace(item.CounterpartyGstin) && !GstinValidator.IsValid(item.CounterpartyGstin))
			{
				issues.Add(new GstTaxAuditIssue(GstTaxAuditSeverity.Error, item.InvoiceNo, item.InvoiceDate, "Invalid GSTIN", "B2B counterparty GSTIN '" + item.CounterpartyGstin + "' fails the 15-character checksum."));
			}
			foreach (GstLine item2 in item.Lines.Where((GstLine line) => string.IsNullOrWhiteSpace(line.HsnCode)))
			{
				issues.Add(new GstTaxAuditIssue(GstTaxAuditSeverity.Error, item.InvoiceNo, item.InvoiceDate, "Missing HSN", "Line '" + item2.Description + "' has no HSN code."));
			}
			decimal num = Round(item.Lines.Sum((GstLine line) => line.Taxable));
			decimal num2 = Round(item.Lines.Sum((GstLine line) => line.Cgst));
			decimal num3 = Round(item.Lines.Sum((GstLine line) => line.Sgst));
			decimal num4 = Round(item.Lines.Sum((GstLine line) => line.Igst));
			decimal num5 = Round(item.Lines.Sum((GstLine line) => line.LineTotal));
			if (Math.Abs(num - item.Taxable) > 0.05m || Math.Abs(num2 + num3 + num4 - item.TaxAmount) > 0.10m || Math.Abs(num5 - item.InvoiceValue) > 0.10m)
			{
				issues.Add(new GstTaxAuditIssue(GstTaxAuditSeverity.Warning, item.InvoiceNo, item.InvoiceDate, "Rounding / total mismatch", $"Line totals (taxable {num:0.00}, tax {num2 + num3 + num4:0.00}, value {num5:0.00}) do not match invoice header (taxable {item.Taxable:0.00}, tax {item.TaxAmount:0.00}, value {item.InvoiceValue:0.00})."));
			}
			foreach (GstLine line in item.Lines)
			{
				decimal num6 = Round(line.Taxable * line.Rate / 100m);
				decimal num7 = Round(line.Cgst + line.Sgst + line.Igst);
				if (Math.Abs(num6 - num7) > 0.05m)
				{
					issues.Add(new GstTaxAuditIssue(GstTaxAuditSeverity.Warning, item.InvoiceNo, item.InvoiceDate, "Rate mismatch", $"'{line.Description}' @ {line.Rate:0.##}% expects tax {num6:0.00} but has {num7:0.00}."));
				}
			}
		}
		GstTaxAuditIssue[] array = (from issue in issues
			orderby issue.Severity descending, issue.InvoiceDate, issue.InvoiceNo
			select issue).ToArray();
		bool flag = array.Any((GstTaxAuditIssue issue) => issue.Severity == GstTaxAuditSeverity.Error);
		bool isClean = array.Length == 0;
		string statusLabel;
		if (array.Length != 0)
		{
			statusLabel = (flag ? $"{array.Count((GstTaxAuditIssue issue) => issue.Severity == GstTaxAuditSeverity.Error)} error(s), {array.Count((GstTaxAuditIssue issue) => issue.Severity == GstTaxAuditSeverity.Warning)} warning(s) — fix before filing" : $"{array.Length} warning(s) — review before filing");
		}
		else
		{
			statusLabel = "Clean / Ready to Export";
		}
		return new GstTaxAuditResult(isClean, statusLabel, array);
	}

	public async Task<GstReturnDashboardSummary> GetDashboardSummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePeriod(from, to);
		IReadOnlyList<GstDocument> sales = await LoadDocumentsAsync(from, to, includeCancelled: false, cancellationToken);
		decimal value = await LoadPurchaseItcAsync(from, to, cancellationToken);
		decimal totalTaxableTurnover = Round(sales.Sum((GstDocument item) => item.Taxable));
		decimal num = Round(sales.Sum((GstDocument item) => item.Cgst));
		decimal num2 = Round(sales.Sum((GstDocument item) => item.Sgst));
		decimal num3 = Round(sales.Sum((GstDocument item) => item.Igst));
		decimal num4 = Round(value);
		decimal netGstPayable = Round(num + num2 + num3 - num4);
		return new GstReturnDashboardSummary(totalTaxableTurnover, num, num2, num3, num4, netGstPayable);
	}

	public async Task<IReadOnlyList<Gstr3bWorksheetRow>> GetGstr3bWorksheetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePeriod(from, to);
		IReadOnlyList<GstDocument> sales = await LoadDocumentsAsync(from, to, includeCancelled: false, cancellationToken);
		(decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst) purchases = await LoadPurchaseTaxBreakupAsync(from, to, cancellationToken);
		GstReturnDashboardSummary gstReturnDashboardSummary = await GetDashboardSummaryAsync(from, to, cancellationToken);
		decimal taxableValue = Round(sales.Sum((GstDocument item) => item.Taxable));
		decimal integratedTax = Round(sales.Sum((GstDocument item) => item.Igst));
		decimal centralTax = Round(sales.Sum((GstDocument item) => item.Cgst));
		decimal stateTax = Round(sales.Sum((GstDocument item) => item.Sgst));
		return new _003C_003Ez__ReadOnlyArray<Gstr3bWorksheetRow>(new Gstr3bWorksheetRow[7]
		{
			new Gstr3bWorksheetRow("3.1(a)", "Outward taxable supplies (other than zero rated, nil rated and exempted)", taxableValue, integratedTax, centralTax, stateTax, 0m),
			new Gstr3bWorksheetRow("3.1(b)", "Outward taxable supplies (zero rated) — not tracked separately in PharmaBill", 0m, 0m, 0m, 0m, 0m),
			new Gstr3bWorksheetRow("3.1(c)", "Other outward supplies (nil rated, exempted) — not tracked separately", 0m, 0m, 0m, 0m, 0m),
			new Gstr3bWorksheetRow("3.1(d)", "Inward supplies (liable to reverse charge) — not tracked separately", 0m, 0m, 0m, 0m, 0m),
			new Gstr3bWorksheetRow("3.1(e)", "Non-GST outward supplies — not tracked separately", 0m, 0m, 0m, 0m, 0m),
			new Gstr3bWorksheetRow("4(A)", "ITC available — purchases (eligible input tax credit estimate)", purchases.Taxable, purchases.Igst, purchases.Cgst, purchases.Sgst, 0m),
			new Gstr3bWorksheetRow("5", "Net GST payable (output − eligible ITC estimate)", gstReturnDashboardSummary.TotalTaxableTurnover, Math.Max(0m, gstReturnDashboardSummary.OutputIgst - purchases.Igst), Math.Max(0m, gstReturnDashboardSummary.OutputCgst - purchases.Cgst), Math.Max(0m, gstReturnDashboardSummary.OutputSgst - purchases.Sgst), 0m)
		});
	}

	public async Task<string> ExportGstr1OfflineJsonAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePeriod(from, to);
		PharmacyProfile pharmacyProfile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		string gstin = (string.IsNullOrWhiteSpace(pharmacyProfile.Gstin) ? string.Empty : GstinValidator.Normalize(pharmacyProfile.Gstin));
		string pharmacyState = IndianGstStateCodes.Resolve(pharmacyProfile.State, pharmacyProfile.Gstin);
		IReadOnlyList<GstDocument> posted = await LoadDocumentsAsync(from, to, includeCancelled: false, cancellationToken);
		IReadOnlyList<GstDocument> documents = await LoadDocumentsAsync(from, to, includeCancelled: true, cancellationToken);
		List<object> value = BuildB2b(posted, pharmacyState);
		List<object> list = BuildB2cl(posted, pharmacyState);
		List<object> value2 = BuildB2cs(posted, pharmacyState, list);
		List<object> value3 = BuildHsn(posted);
		Dictionary<string, object> value4 = BuildDocIssue(documents);
		return JsonSerializer.Serialize(new Dictionary<string, object>
		{
			["gstin"] = gstin,
			["fp"] = $"{from.Month:00}{from.Year}",
			["version"] = "GST3.1.6",
			["hash"] = "hash",
			["b2b"] = value,
			["b2cl"] = list,
			["b2cs"] = value2,
			["hsn"] = new Dictionary<string, object> { ["data"] = value3 },
			["doc_issue"] = value4,
			["pharmabill_note"] = "Generated for GST portal offline utility import. Verify with your CA before filing. Retail CGST/SGST is estimated from inclusive tax; confirm inter-state B2C (B2CL) thresholds for the return period."
		}, JsonOptions);
	}

	private static List<object> BuildB2b(IReadOnlyList<GstDocument> documents, string pharmacyPos)
	{
		return (from document in documents
			where !string.IsNullOrWhiteSpace(document.CounterpartyGstin) && GstinValidator.IsValid(document.CounterpartyGstin)
			group document by GstinValidator.Normalize(document.CounterpartyGstin)).Select((Func<IGrouping<string, GstDocument>, object>)((IGrouping<string, GstDocument> group) => new Dictionary<string, object>
		{
			["ctin"] = group.Key,
			["inv"] = group.Select((GstDocument document) => BuildInvoice(document, pharmacyPos)).ToArray()
		})).ToList();
	}

	private static List<object> BuildB2cl(IReadOnlyList<GstDocument> documents, string pharmacyPos)
	{
		return (from document in documents
			where string.IsNullOrWhiteSpace(document.CounterpartyGstin) || !GstinValidator.IsValid(document.CounterpartyGstin)
			where !IndianGstStateCodes.IsSameState(pharmacyPos, document.PlaceOfSupply, null, null) && document.InvoiceValue > 250000m
			group document by document.PlaceOfSupply).Select((Func<IGrouping<string, GstDocument>, object>)((IGrouping<string, GstDocument> group) => new Dictionary<string, object>
		{
			["pos"] = group.Key,
			["inv"] = group.Select((GstDocument document) => BuildInvoice(document, pharmacyPos, forB2cl: true)).ToArray()
		})).ToList();
	}

	private static List<object> BuildB2cs(IReadOnlyList<GstDocument> documents, string pharmacyPos, IReadOnlyList<object> b2cl)
	{
		HashSet<string> b2clInvoiceNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Dictionary<string, object> item2 in b2cl.OfType<Dictionary<string, object>>())
		{
			if (!item2.TryGetValue("inv", out var value) || !(value is IEnumerable<object> source))
			{
				continue;
			}
			foreach (Dictionary<string, object> item3 in source.OfType<Dictionary<string, object>>())
			{
				if (item3.TryGetValue("inum", out var value2) && value2 is string item)
				{
					b2clInvoiceNos.Add(item);
				}
			}
		}
		return (from row in (from document in documents
				where string.IsNullOrWhiteSpace(document.CounterpartyGstin) || !GstinValidator.IsValid(document.CounterpartyGstin)
				where !b2clInvoiceNos.Contains(document.InvoiceNo)
				select document).SelectMany((GstDocument document) => document.Lines.Select((GstLine line) => new
			{
				PlaceOfSupply = document.PlaceOfSupply,
				Intra = IndianGstStateCodes.IsSameState(pharmacyPos, document.PlaceOfSupply, null, null),
				Rate = line.Rate,
				Taxable = line.Taxable,
				Cgst = line.Cgst,
				Sgst = line.Sgst,
				Igst = line.Igst
			}))
			group row by new { row.PlaceOfSupply, row.Intra, row.Rate } into @group
			select (object)new Dictionary<string, object>
			{
				["sply_ty"] = (@group.Key.Intra ? "INTRA" : "INTER"),
				["pos"] = @group.Key.PlaceOfSupply,
				["typ"] = "OE",
				["rt"] = @group.Key.Rate,
				["txval"] = Round(@group.Sum(anon => anon.Taxable)),
				["iamt"] = Round(@group.Sum(anon => anon.Igst)),
				["camt"] = Round(@group.Sum(anon => anon.Cgst)),
				["samt"] = Round(@group.Sum(anon => anon.Sgst)),
				["csamt"] = 0m
			}).ToList();
	}

	private static List<object> BuildHsn(IReadOnlyList<GstDocument> documents)
	{
		int index = 1;
		return (from line in documents.SelectMany((GstDocument document) => document.Lines)
			group line by new
			{
				Hsn = (string.IsNullOrWhiteSpace(line.HsnCode) ? "NOHSN" : line.HsnCode.Trim()),
				Rate = line.Rate
			} into @group
			orderby @group.Key.Hsn, @group.Key.Rate
			select (object)new Dictionary<string, object>
			{
				["num"] = index++,
				["hsn_sc"] = @group.Key.Hsn,
				["desc"] = ((@group.Key.Hsn == "3004") ? "Medicaments (pharma formulations)" : string.Empty),
				["uqc"] = "NOS-NUMBERS",
				["qty"] = Round(@group.Sum((GstLine item) => item.Quantity)),
				["rt"] = @group.Key.Rate,
				["txval"] = Round(@group.Sum((GstLine item) => item.Taxable)),
				["iamt"] = Round(@group.Sum((GstLine item) => item.Igst)),
				["camt"] = Round(@group.Sum((GstLine item) => item.Cgst)),
				["samt"] = Round(@group.Sum((GstLine item) => item.Sgst)),
				["csamt"] = 0m
			}).ToList();
	}

	private static Dictionary<string, object?> BuildDocIssue(IReadOnlyList<GstDocument> documents)
	{
		GstDocument[] source = (from item in documents
			where !item.IsCancelled
			orderby item.InvoiceNo
			select item).ToArray();
		int num = documents.Count((GstDocument item) => item.IsCancelled);
		string value = source.FirstOrDefault()?.InvoiceNo ?? documents.MinBy((GstDocument item) => item.InvoiceNo)?.InvoiceNo ?? string.Empty;
		string value2 = source.LastOrDefault()?.InvoiceNo ?? documents.MaxBy((GstDocument item) => item.InvoiceNo)?.InvoiceNo ?? string.Empty;
		int count = documents.Count;
		Dictionary<string, object> dictionary = new Dictionary<string, object>();
		dictionary["doc_det"] = new object[1]
		{
			new Dictionary<string, object>
			{
				["doc_num"] = 1,
				["doc_typ"] = "Invoices for outward supply",
				["docs"] = new object[1]
				{
					new Dictionary<string, object>
					{
						["num"] = 1,
						["from"] = value,
						["to"] = value2,
						["totnum"] = count,
						["cancel"] = num,
						["net_issue"] = Math.Max(0, count - num)
					}
				}
			}
		};
		return dictionary;
	}

	private static Dictionary<string, object?> BuildInvoice(GstDocument document, string pharmacyPos, bool forB2cl = false)
	{
		Dictionary<string, object>[] value = (from line in document.Lines
			group line by line.Rate).Select((IGrouping<decimal, GstLine> group, int index) => new Dictionary<string, object>
		{
			["num"] = index + 1,
			["itm_det"] = new Dictionary<string, object>
			{
				["rt"] = group.Key,
				["txval"] = Round(group.Sum((GstLine item) => item.Taxable)),
				["iamt"] = Round(group.Sum((GstLine item) => item.Igst)),
				["camt"] = Round(group.Sum((GstLine item) => item.Cgst)),
				["samt"] = Round(group.Sum((GstLine item) => item.Sgst)),
				["csamt"] = 0m
			}
		}).ToArray();
		Dictionary<string, object> dictionary = new Dictionary<string, object>
		{
			["inum"] = document.InvoiceNo,
			["idt"] = document.InvoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
			["val"] = document.InvoiceValue,
			["pos"] = document.PlaceOfSupply,
			["rchrg"] = "N",
			["itms"] = value
		};
		if (!forB2cl)
		{
			dictionary["inv_typ"] = "R";
		}
		return dictionary;
	}

	private async Task<IReadOnlyList<GstDocument>> LoadDocumentsAsync(DateOnly from, DateOnly to, bool includeCancelled, CancellationToken cancellationToken)
	{
		PharmacyProfile pharmacyProfile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		string pharmacyPos = IndianGstStateCodes.Resolve(pharmacyProfile.State, pharmacyProfile.Gstin);
		List<GstDocument> documents = new List<GstDocument>();
		IQueryable<WholesaleInvoice> source = context.WholesaleInvoices.AsNoTracking().AsQueryable();
		if (!includeCancelled)
		{
			source = source.Where((WholesaleInvoice wholesaleInvoice) => wholesaleInvoice.Status == "Posted");
		}
		List<WholesaleInvoice> wholesale = (await source.ToListAsync(cancellationToken)).Where((WholesaleInvoice wholesaleInvoice) =>
		{
			DateOnly dateOnly = DateOnly.FromDateTime(wholesaleInvoice.InvoiceAtUtc.ToLocalTime());
			return dateOnly >= from && dateOnly <= to;
		}).ToList();
		Guid[] wholesaleIds = wholesale.Select((WholesaleInvoice wholesaleInvoice) => wholesaleInvoice.Id).ToArray();
		List<WholesaleInvoiceItem> wholesaleItems = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where wholesaleIds.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(cancellationToken);
		Dictionary<Guid, Customer> customers = await context.Customers.AsNoTracking().ToDictionaryAsync((Customer customer2) => customer2.Id, cancellationToken);
		Guid[] drugIds = wholesaleItems.Select((WholesaleInvoiceItem item) => item.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
			where drugIds.Contains(drug.Id)
			select drug).ToDictionaryAsync((Drug drug) => drug.Id, cancellationToken);
		foreach (WholesaleInvoice invoice in wholesale)
		{
			Customer customer = customers[invoice.CustomerId];
			string placeOfSupply = IndianGstStateCodes.Resolve(customer.State, customer.Gstin);
			if (string.IsNullOrWhiteSpace(customer.State) && string.IsNullOrWhiteSpace(customer.Gstin))
			{
				placeOfSupply = pharmacyPos;
			}
			GstLine[] lines = wholesaleItems.Where((WholesaleInvoiceItem item) => item.WholesaleInvoiceId == invoice.Id).Select((WholesaleInvoiceItem item) =>
			{
				Drug valueOrDefault = drugs.GetValueOrDefault(item.DrugId);
				return new GstLine(valueOrDefault?.Name ?? "Item", valueOrDefault?.HsnCode, item.Quantity + item.FreeQuantity, item.TaxRate, item.TaxableAmount, item.CgstAmount, item.SgstAmount, item.IgstAmount, item.LineTotal);
			}).ToArray();
			documents.Add(new GstDocument(invoice.InvoiceNo, DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()), customer.Gstin, placeOfSupply, invoice.Subtotal, invoice.TaxAmount, invoice.CgstAmount, invoice.SgstAmount, invoice.IgstAmount, invoice.TotalAmount, invoice.Status == "Cancelled", lines));
		}
		DateTime fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		List<Sale> retailSales = await (from sale2 in context.Sales.AsNoTracking()
			where sale2.SaleAtUtc >= fromUtc && sale2.SaleAtUtc < toUtcExclusive && !sale2.IsDeleted
			select sale2).ToListAsync(cancellationToken);
		Guid[] saleIds = retailSales.Select((Sale sale2) => sale2.Id).ToArray();
		List<SaleItem> saleItems = await (from item in context.SaleItems.AsNoTracking()
			where saleIds.Contains(item.SaleId)
			select item).ToListAsync(cancellationToken);
		Guid[] retailDrugIds = saleItems.Select((SaleItem item) => item.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> retailDrugs = await (from drug in context.Drugs.AsNoTracking()
			where retailDrugIds.Contains(drug.Id)
			select drug).ToDictionaryAsync((Drug drug) => drug.Id, cancellationToken);
		foreach (Sale sale in retailSales)
		{
			GstLine[] array = saleItems.Where((SaleItem item) => item.SaleId == sale.Id).Select((SaleItem item) =>
			{
				Drug valueOrDefault = retailDrugs.GetValueOrDefault(item.DrugId);
				InclusiveTaxLine inclusiveTaxLine = RetailTaxCalculator.SplitInclusive(item.Quantity, item.UnitPrice, item.TaxRate, item.DiscountAmount);
				decimal num3 = Round(inclusiveTaxLine.TaxAmount / 2m);
				return new GstLine(valueOrDefault?.Name ?? "Medicine", valueOrDefault?.HsnCode, item.Quantity, item.TaxRate, inclusiveTaxLine.NetAmount, num3, inclusiveTaxLine.TaxAmount - num3, 0m, item.LineTotal);
			}).ToArray();
			decimal num = Round(array.Sum((GstLine line) => line.Cgst));
			decimal num2 = Round(array.Sum((GstLine line) => line.Sgst));
			documents.Add(new GstDocument(sale.InvoiceNo, DateOnly.FromDateTime(sale.SaleAtUtc.ToLocalTime()), null, pharmacyPos, Round(array.Sum((GstLine line) => line.Taxable)), Round(num + num2), num, num2, 0m, sale.TotalAmount, sale.IsDeleted, array));
		}
		return (from document in documents
			orderby document.InvoiceDate, document.InvoiceNo
			select document).ToArray();
	}

	private async Task<decimal> LoadPurchaseItcAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
	{
		(decimal, decimal, decimal, decimal) tuple = await LoadPurchaseTaxBreakupAsync(from, to, cancellationToken);
		return Round(tuple.Item2 + tuple.Item3 + tuple.Item4);
	}

	private async Task<(decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst)> LoadPurchaseTaxBreakupAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
	{
		List<PurchaseInvoice> invoices = await (from invoice in context.PurchaseInvoices.AsNoTracking()
			where (invoice.Status == PurchaseInvoice.PostedStatus || invoice.Status == PurchaseInvoice.CommittedStatus) && invoice.InvoiceDate >= @from && invoice.InvoiceDate <= to
			select invoice).ToListAsync(cancellationToken);
		Guid[] invoiceIds = invoices.Select((PurchaseInvoice invoice) => invoice.Id).ToArray();
		List<PurchaseItem> items = await (from purchaseItem in context.PurchaseItems.AsNoTracking()
			where invoiceIds.Contains(purchaseItem.PurchaseInvoiceId)
			select purchaseItem).ToListAsync(cancellationToken);
		PharmacyProfile pharmacyProfile = await context.PharmacyProfiles.AsNoTracking().SingleAsync(cancellationToken);
		string pharmacyPos = IndianGstStateCodes.Resolve(pharmacyProfile.State, pharmacyProfile.Gstin);
		Dictionary<Guid, Supplier> dictionary = await context.Suppliers.AsNoTracking().ToDictionaryAsync((Supplier supplier2) => supplier2.Id, cancellationToken);
		decimal value = 0m;
		decimal value2 = 0m;
		decimal value3 = 0m;
		decimal value4 = 0m;
		foreach (PurchaseItem item in items)
		{
			PurchaseInvoice purchaseInvoice = invoices.Single((PurchaseInvoice row) => row.Id == item.PurchaseInvoiceId);
			Supplier supplier = dictionary[purchaseInvoice.SupplierId];
			decimal num = Round(item.Quantity * item.UnitPrice - item.DiscountAmount);
			decimal num2 = Round(num * item.TaxRate / 100m);
			value += num;
			string a = IndianGstStateCodes.Resolve(null, supplier.Gstin);
			if (!string.IsNullOrWhiteSpace(supplier.Gstin) && !string.Equals(a, pharmacyPos, StringComparison.Ordinal))
			{
				value4 += num2;
				continue;
			}
			decimal num3 = Round(num2 / 2m);
			value2 += num3;
			value3 += num2 - num3;
		}
		return (Taxable: Round(value), Cgst: Round(value2), Sgst: Round(value3), Igst: Round(value4));
	}

	private static void ValidatePeriod(DateOnly from, DateOnly to)
	{
		if (to < from)
		{
			throw new ArgumentException("The GST return period end date must be on or after the start date.");
		}
	}

	private static decimal Round(decimal value)
	{
		return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
	}
}
