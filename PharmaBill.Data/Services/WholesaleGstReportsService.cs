using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class WholesaleGstReportsService(PharmaBillDbContext context)
{
	public async Task<IReadOnlyList<WholesalePurchaseRegisterRow>> GetPurchaseRegisterAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePeriod(from, to);
		List<PurchaseInvoice> invoices = await (from invoice in context.PurchaseInvoices.AsNoTracking()
			where (invoice.Status == PurchaseInvoice.PostedStatus || invoice.Status == PurchaseInvoice.CommittedStatus) && invoice.InvoiceDate >= @from && invoice.InvoiceDate <= to
			select invoice).ToListAsync(cancellationToken);
		Guid[] invoiceIds = invoices.Select((PurchaseInvoice invoice) => invoice.Id).ToArray();
		Dictionary<Guid, Supplier> suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync((Supplier item) => item.Id, cancellationToken);
		List<PurchaseItem> items = await (from item in context.PurchaseItems.AsNoTracking()
			where invoiceIds.Contains(item.PurchaseInvoiceId)
			select item).ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, cancellationToken);
		return (from row in items.Select((PurchaseItem item) =>
			{
				PurchaseInvoice purchaseInvoice = invoices.Single((PurchaseInvoice row) => row.Id == item.PurchaseInvoiceId);
				Supplier supplier = suppliers[purchaseInvoice.SupplierId];
				decimal num = decimal.Round(item.Quantity * item.UnitPrice - item.DiscountAmount, 2, MidpointRounding.AwayFromZero);
				decimal taxAmount = decimal.Round(num * item.TaxRate / 100m, 2, MidpointRounding.AwayFromZero);
				return new WholesalePurchaseRegisterRow(purchaseInvoice.InvoiceNo, purchaseInvoice.InvoiceDate, supplier.Name, supplier.Gstin, drugs[item.DrugId].HsnCode ?? string.Empty, num, taxAmount, item.LineTotal);
			})
			orderby row.InvoiceDate, row.InvoiceNo
			select row).ToArray();
	}

	public async Task<IReadOnlyList<WholesaleSalesRegisterRow>> GetSalesRegisterAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePeriod(from, to);
		List<WholesaleInvoice> invoices = await (from invoice in context.WholesaleInvoices.AsNoTracking()
			where invoice.Status == "Posted"
			select invoice).ToListAsync(cancellationToken);
		Guid[] invoiceIds = (from invoice in invoices
			where DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()) >= @from && DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()) <= to
			select invoice.Id).ToArray();
		Dictionary<Guid, Customer> customers = await context.Customers.AsNoTracking().ToDictionaryAsync((Customer customer2) => customer2.Id, cancellationToken);
		List<WholesaleInvoiceItem> items = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where invoiceIds.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> dictionary = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug drug) => drug.Id, cancellationToken);
		IEnumerable<IGrouping<Guid, WholesaleInvoiceItem>> enumerable = from item in items
			group item by item.WholesaleInvoiceId;
		List<WholesaleSalesRegisterRow> list = new List<WholesaleSalesRegisterRow>();
		foreach (IGrouping<Guid, WholesaleInvoiceItem> group in enumerable)
		{
			WholesaleInvoice wholesaleInvoice = invoices.Single((WholesaleInvoice item) => item.Id == group.Key);
			Customer customer = customers[wholesaleInvoice.CustomerId];
			foreach (WholesaleInvoiceItem item in group)
			{
				list.Add(new WholesaleSalesRegisterRow(wholesaleInvoice.InvoiceNo, wholesaleInvoice.InvoiceAtUtc, customer.Name, customer.Gstin, dictionary[item.DrugId].HsnCode ?? string.Empty, item.TaxableAmount, item.CgstAmount, item.SgstAmount, item.IgstAmount, item.LineTotal));
			}
		}
		return (from row in list
			orderby row.InvoiceAtUtc, row.InvoiceNo
			select row).ToArray();
	}

	public async Task<IReadOnlyList<GstRateSummary>> GetHsnSummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePeriod(from, to);
		DateTime fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime toUtcExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		Guid[] invoiceIds = await (from invoice in context.WholesaleInvoices.AsNoTracking()
			where invoice.Status == "Posted" && invoice.InvoiceAtUtc >= fromUtc && invoice.InvoiceAtUtc < toUtcExclusive
			select invoice.Id).ToArrayAsync(cancellationToken);
		List<WholesaleInvoiceItem> items = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where invoiceIds.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(cancellationToken);
		Guid[] drugIds = items.Select((WholesaleInvoiceItem item) => item.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
			where drugIds.Contains(drug.Id)
			select drug).ToDictionaryAsync((Drug drug) => drug.Id, cancellationToken);
		return (from item in items
			group item by new
			{
				Hsn = (drugs[item.DrugId].HsnCode ?? string.Empty),
				TaxRate = item.TaxRate
			} into @group
			select new GstRateSummary(@group.Key.Hsn, @group.Key.TaxRate, @group.Sum((WholesaleInvoiceItem item) => item.TaxableAmount), @group.Sum((WholesaleInvoiceItem item) => item.CgstAmount), @group.Sum((WholesaleInvoiceItem item) => item.SgstAmount), @group.Sum((WholesaleInvoiceItem item) => item.IgstAmount), @group.Sum((WholesaleInvoiceItem item) => item.LineTotal))).OrderBy((GstRateSummary row) => row.Hsn, StringComparer.OrdinalIgnoreCase).ThenBy((GstRateSummary row) => row.TaxRate).ToArray();
	}

	public async Task<Gstr3bReferenceSummary> GetGstr3bReferenceSummaryAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<WholesaleSalesRegisterRow> source = await GetSalesRegisterAsync(from, to, cancellationToken);
		return new Gstr3bReferenceSummary("For reference only; verify with your CA.", source.Sum((WholesaleSalesRegisterRow row) => row.TaxableAmount), source.Sum((WholesaleSalesRegisterRow row) => row.Cgst), source.Sum((WholesaleSalesRegisterRow row) => row.Sgst), source.Sum((WholesaleSalesRegisterRow row) => row.Igst));
	}

	public async Task<string> ExportGstr1JsonAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		var invoices = from row in await GetSalesRegisterAsync(@from, to, cancellationToken)
			group row by row.InvoiceNo into @group
			select new
			{
				invoiceNo = @group.Key,
				date = DateOnly.FromDateTime(@group.First().InvoiceAtUtc.ToLocalTime()).ToString("yyyy-MM-dd"),
				customer = @group.First().CustomerName,
				gstin = @group.First().Gstin,
				items = @group.Select((WholesaleSalesRegisterRow row) => new
				{
					hsn = row.Hsn,
					taxable = row.TaxableAmount,
					cgst = row.Cgst,
					sgst = row.Sgst,
					igst = row.Igst,
					total = row.Total
				})
			};
		return JsonSerializer.Serialize(new
		{
			format = "GSTR-1-style reference export",
			warning = "Not a government filing format. Verify with your CA before use.",
			from = from,
			to = to,
			invoices = invoices
		}, new JsonSerializerOptions
		{
			WriteIndented = true
		});
	}

	public async Task<string> ExportGstr1CsvAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default(CancellationToken))
	{
		IReadOnlyList<WholesaleSalesRegisterRow> readOnlyList = await GetSalesRegisterAsync(from, to, cancellationToken);
		StringBuilder stringBuilder = new StringBuilder("InvoiceNo,InvoiceDate,Customer,GSTIN,HSN,Taxable,CGST,SGST,IGST,Total\r\n");
		foreach (WholesaleSalesRegisterRow item in readOnlyList)
		{
			string[] source = new string[10]
			{
				item.InvoiceNo,
				DateOnly.FromDateTime(item.InvoiceAtUtc.ToLocalTime()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				item.CustomerName,
				item.Gstin ?? string.Empty,
				item.Hsn,
				item.TaxableAmount.ToString("0.00", CultureInfo.InvariantCulture),
				item.Cgst.ToString("0.00", CultureInfo.InvariantCulture),
				item.Sgst.ToString("0.00", CultureInfo.InvariantCulture),
				item.Igst.ToString("0.00", CultureInfo.InvariantCulture),
				item.Total.ToString("0.00", CultureInfo.InvariantCulture)
			};
			stringBuilder.AppendLine(string.Join(",", source.Select(EscapeCsv)));
		}
		return stringBuilder.ToString();
	}

	public async Task<IReadOnlyList<WholesaleSalesAnalysisRow>> GetSalesAnalysisAsync(DateOnly from, DateOnly to, string dimension, CancellationToken cancellationToken = default(CancellationToken))
	{
		ValidatePeriod(from, to);
		WholesaleInvoice[] source = (await (from invoice in context.WholesaleInvoices.AsNoTracking()
			where invoice.Status == "Posted"
			select invoice).ToListAsync(cancellationToken)).Where((WholesaleInvoice invoice) => DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()) >= from && DateOnly.FromDateTime(invoice.InvoiceAtUtc.ToLocalTime()) <= to).ToArray();
		Guid[] invoiceIds = source.Select((WholesaleInvoice invoice) => invoice.Id).ToArray();
		Dictionary<Guid, WholesaleInvoice> invoiceById = source.ToDictionary((WholesaleInvoice invoice) => invoice.Id);
		Dictionary<Guid, Customer> customers = await context.Customers.AsNoTracking().ToDictionaryAsync((Customer item) => item.Id, cancellationToken);
		List<WholesaleInvoiceItem> items = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where invoiceIds.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, cancellationToken);
		Dictionary<Guid, Batch> batches = await context.Batches.AsNoTracking().ToDictionaryAsync((Batch item) => item.Id, cancellationToken);
		Guid[] catalogIds = (from drug in drugs.Values
			where drug.CatalogMedicineId.HasValue
			select drug.CatalogMedicineId.Value).Distinct().ToArray();
		Dictionary<Guid, string> manufacturers = await (from item in context.CatalogMedicines.AsNoTracking()
			where catalogIds.Contains(item.Id)
			select item).ToDictionaryAsync((CatalogMedicine item) => item.Id, (CatalogMedicine item) => item.Manufacturer ?? "Unknown company", cancellationToken);
		return (from row in (from item in items
				group item by GetDimension(dimension, invoiceById[item.WholesaleInvoiceId], customers, drugs[item.DrugId], manufacturers)).Select((IGrouping<string, WholesaleInvoiceItem> @group) =>
			{
				decimal num = @group.Sum((WholesaleInvoiceItem item) => item.LineTotal);
				decimal num2 = @group.Sum((WholesaleInvoiceItem item) => batches[item.BatchId].PurchasePrice * (item.Quantity + item.FreeQuantity));
				return new WholesaleSalesAnalysisRow(@group.Key, @group.Sum((WholesaleInvoiceItem item) => item.Quantity + item.FreeQuantity), num, num2, num - num2);
			})
			orderby row.Sales descending
			select row).ToArray();
	}

	private static string GetDimension(string dimension, WholesaleInvoice invoice, IReadOnlyDictionary<Guid, Customer> customers, Drug drug, IReadOnlyDictionary<Guid, string> manufacturers)
	{
		return dimension.ToLowerInvariant() switch
		{
			"customer" => customers[invoice.CustomerId].Name, 
			"item" => drug.Name, 
			"company" => drug.CatalogMedicineId.HasValue ? manufacturers.GetValueOrDefault(drug.CatalogMedicineId.Value, "Unknown company") : "Unknown company", 
			"salesman" => customers[invoice.CustomerId].Salesman ?? "Unassigned", 
			"area" => customers[invoice.CustomerId].Route ?? "Unassigned", 
			_ => throw new ArgumentException("Dimension must be customer, item, company, salesman or area.", "dimension"), 
		};
	}

	private static string EscapeCsv(string value)
	{
		return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
	}

	private static void ValidatePeriod(DateOnly from, DateOnly to)
	{
		if (to < from)
		{
			throw new ArgumentException("Report end date cannot precede its start date.");
		}
	}
}
