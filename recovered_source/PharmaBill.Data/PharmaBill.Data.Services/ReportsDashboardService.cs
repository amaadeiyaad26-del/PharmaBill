using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class ReportsDashboardService(PharmaBillDbContext context, BranchService branchService)
{
	private sealed record SalesLineFact(Guid DocumentId, string RowKey, DateTime AtUtc, string InvoiceNo, string Party, string Contact, string Gstin, string Item, string Brand, string Manufacturer, string BatchNo, string Expiry, decimal Quantity, decimal UnitPrice, decimal Taxable, decimal TaxAmount, decimal LineTotal, string BranchCode);

	public const int TrendDays = 7;

	public const int FirstBusinessHour = 9;

	public const int LastBusinessHour = 22;

	public const int PeakHoursLookbackDays = 30;

	public async Task<DashboardAnalytics> GetDashboardAnalyticsAsync(DateTime nowLocal, CancellationToken cancellationToken = default(CancellationToken))
	{
		DateOnly dateOnly = DateOnly.FromDateTime(nowLocal);
		DateTime tomorrowUtc = dateOnly.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateOnly monthStart = new DateOnly(dateOnly.Year, dateOnly.Month, 1);
		DateOnly trendStart = dateOnly.AddDays(-6);
		DateOnly lookbackStart = dateOnly.AddDays(-29);
		DateTime earliestUtc = new DateOnly[3] { monthStart, trendStart, lookbackStart }.Min().ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		var retail = await (from item in context.Sales.AsNoTracking()
			where item.SaleAtUtc >= earliestUtc && item.SaleAtUtc < tomorrowUtc
			select new
			{
				Id = item.Id,
				At = item.SaleAtUtc,
				TotalAmount = item.TotalAmount
			}).ToListAsync(cancellationToken);
		var source = await (from item in context.WholesaleInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceAtUtc >= earliestUtc && item.InvoiceAtUtc < tomorrowUtc
			select new
			{
				Id = item.Id,
				At = item.InvoiceAtUtc,
				TotalAmount = item.TotalAmount
			}).ToListAsync(cancellationToken);
		List<(Guid Id, DateTime Local, decimal TotalAmount)> documents = retail.Select(item => (Id: item.Id, Local: DateTime.SpecifyKind(item.At, DateTimeKind.Utc).ToLocalTime(), TotalAmount: item.TotalAmount)).Concat(source.Select(item => (Id: item.Id, Local: DateTime.SpecifyKind(item.At, DateTimeKind.Utc).ToLocalTime(), TotalAmount: item.TotalAmount))).ToList();
		List<(DateOnly, decimal)> source2 = (from offset in Enumerable.Range(0, 7)
			select trendStart.AddDays(offset) into day
			select (Day: day, Total: documents.Where(((Guid Id, DateTime Local, decimal TotalAmount) item) => DateOnly.FromDateTime(item.Local) == day).Sum(((Guid Id, DateTime Local, decimal TotalAmount) item) => item.TotalAmount))).ToList();
		decimal dailyMax = source2.Max(((DateOnly Day, decimal Total) item) => item.Total);
		List<ChartBar> dailyBars = source2.Select(((DateOnly Day, decimal Total) item) => new ChartBar(item.Day.ToString("dd MMM"), item.Total, (dailyMax > 0m) ? ((double)(item.Total / dailyMax)) : 0.0)).ToList();
		Dictionary<int, int> hourlyCounts = (from item in documents
			where DateOnly.FromDateTime(item.Local) >= lookbackStart
			group item by item.Local.Hour).ToDictionary((IGrouping<int, (Guid Id, DateTime Local, decimal TotalAmount)> group) => group.Key, (IGrouping<int, (Guid Id, DateTime Local, decimal TotalAmount)> group) => group.Count());
		int hourlyMax = ((hourlyCounts.Count != 0) ? hourlyCounts.Values.Max() : 0);
		List<ChartBar> hourlyBars = (from hour in Enumerable.Range(9, 14)
			select new ChartBar(new DateTime(2000, 1, 1, hour, 0, 0).ToString("h tt"), hourlyCounts.GetValueOrDefault(hour), (hourlyMax > 0) ? ((double)hourlyCounts.GetValueOrDefault(hour) / (double)hourlyMax) : 0.0)).ToList();
		HashSet<Guid> monthDocumentIds = (from item in documents
			where DateOnly.FromDateTime(item.Local) >= monthStart
			select item.Id).ToHashSet();
		var retailItems = await (from item in context.SaleItems.AsNoTracking()
			where monthDocumentIds.Contains(item.SaleId)
			select new { item.DrugId, item.Quantity, item.LineTotal }).ToListAsync(cancellationToken);
		var source3 = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where monthDocumentIds.Contains(item.WholesaleInvoiceId)
			select new
			{
				DrugId = item.DrugId,
				Quantity = item.Quantity + item.FreeQuantity,
				LineTotal = item.LineTotal
			}).ToListAsync(cancellationToken);
		List<(Guid DrugId, decimal Quantity, decimal LineTotal)> lines = retailItems.Select(item => (DrugId: item.DrugId, Quantity: item.Quantity, LineTotal: item.LineTotal)).Concat(source3.Select(item => (DrugId: item.DrugId, Quantity: item.Quantity, LineTotal: item.LineTotal))).ToList();
		Dictionary<Guid, (string Name, string Schedule)> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, (Drug item) => (Name: item.Name, Schedule: item.Schedule), cancellationToken);
		decimal totalUnits = lines.Sum(((Guid DrugId, decimal Quantity, decimal LineTotal) item) => item.Quantity);
		List<TopMedicine> topMedicines = (from item in (from item in lines
				group item by item.DrugId).Select((IGrouping<Guid, (Guid DrugId, decimal Quantity, decimal LineTotal)> @group) =>
			{
				string item;
				if (drugs.TryGetValue(@group.Key, out (string, string) value))
				{
					(item, _) = value;
				}
				else
				{
					item = "Unknown";
				}
				return (Name: item, Units: @group.Sum(((Guid DrugId, decimal Quantity, decimal LineTotal) tuple2) => tuple2.Quantity));
			})
			orderby item.Units descending
			select item).ThenBy(((string Name, decimal Units) item) => item.Name, StringComparer.OrdinalIgnoreCase).Take(5).Select(((string Name, decimal Units) item, int index) => new TopMedicine(index + 1, item.Name, item.Units, (totalUnits > 0m) ? ((double)(item.Units / totalUnits * 100m)) : 0.0))
			.ToList();
		decimal totalAmount = lines.Sum(((Guid DrugId, decimal Quantity, decimal LineTotal) item) => item.LineTotal);
		List<CategoryShare> categories = (from item in lines
			group item by ClassifySchedule(drugs.TryGetValue(item.DrugId, out (string, string) value) ? value.Item2 : null) into @group
			select new CategoryShare(@group.Key, @group.Sum(((Guid DrugId, decimal Quantity, decimal LineTotal) item) => item.LineTotal), (totalAmount > 0m) ? ((double)(@group.Sum(((Guid DrugId, decimal Quantity, decimal LineTotal) item) => item.LineTotal) / totalAmount * 100m)) : 0.0) into item
			orderby item.Amount descending
			select item).ToList();
		return new DashboardAnalytics(dailyBars, hourlyBars, topMedicines, categories);
	}

	public static string ClassifySchedule(string? schedule)
	{
		string text = (schedule ?? string.Empty).Replace("Schedule", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty).ToUpperInvariant();
		bool flag = text.Length == 0;
		if (!flag)
		{
			bool flag2;
			switch (text)
			{
			case "OTC":
			case "NONE":
			case "NA":
			case "N/A":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag)
		{
			return "OTC";
		}
		if (text.Contains("NDPS", StringComparison.Ordinal) || text == "X")
		{
			return "Schedule X / NDPS";
		}
		if (!text.StartsWith('H'))
		{
			return "Other";
		}
		return "Schedule H / H1";
	}

	public async Task<DashboardSnapshot> GetDashboardAsync(DateTime nowLocal, CancellationToken cancellationToken = default(CancellationToken))
	{
		DateOnly today = DateOnly.FromDateTime(nowLocal);
		DateTime todayStartUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime tomorrowUtc = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime monthStartUtc = new DateOnly(today.Year, today.Month, 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		decimal sales = (await (from item in context.Sales.AsNoTracking()
			where item.SaleAtUtc >= todayStartUtc && item.SaleAtUtc < tomorrowUtc
			select item).SumAsync((Expression<Func<Sale, decimal?>>)((Sale item) => item.TotalAmount), cancellationToken)).GetValueOrDefault();
		decimal wholesaleToday = (await (from item in context.WholesaleInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceAtUtc >= todayStartUtc && item.InvoiceAtUtc < tomorrowUtc
			select item).SumAsync((Expression<Func<WholesaleInvoice, decimal?>>)((WholesaleInvoice item) => item.TotalAmount), cancellationToken)).GetValueOrDefault();
		int retailInvoiceCount = await context.Sales.AsNoTracking().CountAsync((Sale item) => item.SaleAtUtc >= todayStartUtc && item.SaleAtUtc < tomorrowUtc, cancellationToken);
		int wholesaleInvoiceCount = await context.WholesaleInvoices.AsNoTracking().CountAsync((WholesaleInvoice item) => item.Status == "Posted" && item.InvoiceAtUtc >= todayStartUtc && item.InvoiceAtUtc < tomorrowUtc, cancellationToken);
		decimal monthRetail = (await (from item in context.Sales.AsNoTracking()
			where item.SaleAtUtc >= monthStartUtc && item.SaleAtUtc < tomorrowUtc
			select item).SumAsync((Expression<Func<Sale, decimal?>>)((Sale item) => item.TotalAmount), cancellationToken)).GetValueOrDefault();
		decimal monthWholesale = (await (from item in context.WholesaleInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceAtUtc >= monthStartUtc && item.InvoiceAtUtc < tomorrowUtc
			select item).SumAsync((Expression<Func<WholesaleInvoice, decimal?>>)((WholesaleInvoice item) => item.TotalAmount), cancellationToken)).GetValueOrDefault();
		DateOnly threshold = today.AddDays(90);
		Guid[] expiredSoon = await (from batch in context.Batches.AsNoTracking()
			where batch.ExpiryDate.HasValue && batch.ExpiryDate.Value >= today && batch.ExpiryDate.Value <= threshold
			select batch.Id).ToArrayAsync(cancellationToken);
		Dictionary<Guid, decimal> balances = await (from item in context.StockMovements.AsNoTracking()
			group item by item.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement item) => item.QuantityChange)
			}).ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
		int expiringSoonCount = (await (from batch in context.Batches.AsNoTracking()
			where expiredSoon.Contains(batch.Id)
			select batch).ToListAsync(cancellationToken)).Count((Batch batch) => balances.GetValueOrDefault(batch.Id) > 0m);
		List<Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive
			select drug).ToListAsync(cancellationToken);
		Dictionary<Guid, decimal> quantities = await (from batch in context.Batches.AsNoTracking()
			join movement in context.StockMovements.AsNoTracking() on batch.Id equals movement.BatchId
			group movement by batch.DrugId into grouped
			select new
			{
				DrugId = grouped.Key,
				Quantity = grouped.Sum((StockMovement item) => item.QuantityChange)
			}).ToDictionaryAsync(item => item.DrugId, item => item.Quantity, cancellationToken);
		int lowStock = drugs.Count((Drug drug) => StockShortage.IsShortage(quantities.GetValueOrDefault(drug.Id), drug.ReorderLevel));
		decimal outstanding = await GetWholesaleOutstandingAsync(today, cancellationToken);
		int registerAlerts = await context.ScheduleRegisterEntries.AsNoTracking().CountAsync((ScheduleRegisterEntry item) => item.EntryAtUtc >= todayStartUtc && item.EntryAtUtc < tomorrowUtc, cancellationToken);
		return new DashboardSnapshot(sales + wholesaleToday, monthRetail + monthWholesale, expiringSoonCount, lowStock, outstanding, registerAlerts, retailInvoiceCount + wholesaleInvoiceCount);
	}

	public Task<ReportTable> GetReportAsync(ReportKind kind, DateOnly from, DateOnly to, ReportBranchScope branchScope, CancellationToken cancellationToken)
	{
		return GetReportAsync(kind, from, to, branchScope, null, cancellationToken);
	}

	public async Task<ReportTable> GetReportAsync(ReportKind kind, DateOnly from, DateOnly to, ReportBranchScope branchScope = ReportBranchScope.CurrentBranchOnly, string? dimensionFilter = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (to < from)
		{
			throw new ArgumentException("Report end date cannot precede its start date.");
		}
		string filter = NormalizeFilter(dimensionFilter);
		ReportTable result;
		switch (kind)
		{
		case ReportKind.Sales:
			result = await SalesSummaryAsync(from, to, branchScope, filter, cancellationToken);
			break;
		case ReportKind.Purchases:
			result = await PurchasesAsync(from, to, branchScope, cancellationToken);
			break;
		case ReportKind.Profit:
			result = await ProfitAsync(from, to, cancellationToken);
			break;
		case ReportKind.GstSummary:
			result = await GstAsync(from, to, cancellationToken);
			break;
		case ReportKind.TopSellingDrugs:
			result = await TopSellingAsync(from, to, cancellationToken);
			break;
		case ReportKind.Expiry:
			result = await ExpiryAsync(from, to, cancellationToken);
			break;
		case ReportKind.LowStock:
			result = await LowStockAsync(cancellationToken);
			break;
		case ReportKind.SupplierWise:
		case ReportKind.PurchaserSupplierWise:
			result = await SupplierWiseDetailAsync(from, to, filter, cancellationToken);
			break;
		case ReportKind.CustomerPartyWise:
			result = await CustomerPartyWiseAsync(from, to, branchScope, filter, cancellationToken);
			break;
		case ReportKind.BatchWise:
			result = await BatchWiseAsync(from, to, branchScope, filter, cancellationToken);
			break;
		case ReportKind.BrandProductWise:
			result = await BrandProductWiseAsync(from, to, branchScope, filter, cancellationToken);
			break;
		case ReportKind.ManufacturerWise:
			result = await ManufacturerWiseAsync(from, to, branchScope, filter, cancellationToken);
			break;
		default:
			throw new ArgumentOutOfRangeException("kind");
		}
		return result;
	}

	public async Task<IReadOnlyList<string>> GetDimensionFilterOptionsAsync(ReportKind kind, DateOnly from, DateOnly to, ReportBranchScope branchScope = ReportBranchScope.CurrentBranchOnly, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<SalesLineFact> source = await LoadSalesLineFactsAsync(from, to, branchScope, cancellationToken);
		IEnumerable<string> source2;
		switch (kind)
		{
		case ReportKind.CustomerPartyWise:
			source2 = source.Select((SalesLineFact item) => item.Party);
			break;
		case ReportKind.BatchWise:
			source2 = from item in source
				select item.BatchNo into value
				where !string.IsNullOrWhiteSpace(value)
				select value;
			break;
		case ReportKind.BrandProductWise:
			source2 = source.Select((SalesLineFact item) => item.Brand);
			break;
		case ReportKind.ManufacturerWise:
			source2 = source.Select((SalesLineFact item) => item.Manufacturer);
			break;
		case ReportKind.SupplierWise:
		case ReportKind.PurchaserSupplierWise:
			source2 = await LoadSupplierNamesAsync(from, to, cancellationToken);
			break;
		default:
			source2 = Array.Empty<string>();
			break;
		}
		return source2.Select((string value) => (!string.IsNullOrWhiteSpace(value)) ? value.Trim() : "(Unspecified)").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy((string value) => value, StringComparer.OrdinalIgnoreCase)
			.Prepend("All")
			.ToArray();
	}

	public static bool UsesDimensionFilter(ReportKind kind)
	{
		if (kind == ReportKind.SupplierWise || (uint)(kind - 100) <= 4u)
		{
			return true;
		}
		return false;
	}

	public static string DimensionFilterLabel(ReportKind kind)
	{
		switch (kind)
		{
		case ReportKind.CustomerPartyWise:
			return "Party / customer";
		case ReportKind.BatchWise:
			return "Batch / B.No";
		case ReportKind.BrandProductWise:
			return "Brand / product";
		case ReportKind.ManufacturerWise:
			return "Manufacturer";
		case ReportKind.SupplierWise:
		case ReportKind.PurchaserSupplierWise:
			return "Supplier";
		default:
			return "Filter";
		}
	}

	private async Task<ReportTable> SalesSummaryAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, string? filter, CancellationToken token)
	{
		List<SalesLineFact> source = await LoadSalesLineFactsAsync(from, to, branchScope, token);
		if (!string.IsNullOrEmpty(filter))
		{
			source = source.Where((SalesLineFact item) => item.Party.Contains(filter, StringComparison.OrdinalIgnoreCase) || item.Item.Contains(filter, StringComparison.OrdinalIgnoreCase) || item.BatchNo.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		List<IReadOnlyList<string>> list = ((IEnumerable<SalesLineFact>)source.OrderBy((SalesLineFact item) => item.AtUtc).ThenBy((SalesLineFact item) => item.InvoiceNo, StringComparer.Ordinal)).Select((Func<SalesLineFact, IReadOnlyList<string>>)((SalesLineFact item) => new _003C_003Ez__ReadOnlyArray<string>(new string[10]
		{
			item.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
			item.InvoiceNo,
			item.Party,
			item.Item,
			item.BatchNo,
			item.Expiry,
			item.Quantity.ToString("0.##"),
			Format(item.UnitPrice),
			Format(item.LineTotal),
			item.BranchCode
		}))).ToList();
		if (branchScope == ReportBranchScope.ConsolidatedNetwork)
		{
			foreach (BranchSalesSummaryDocument item in branchService.LoadPeerSalesSummaries())
			{
				list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[10]
				{
					item.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
					item.BranchCode + " summary",
					item.BranchName,
					$"{item.InvoiceCount} invoice(s) (network)",
					"—",
					"—",
					"—",
					"—",
					Format(item.SalesTotal),
					item.BranchCode
				}));
			}
		}
		List<string> list2 = (from item in source.OrderBy((SalesLineFact item) => item.AtUtc).ThenBy((SalesLineFact item) => item.InvoiceNo, StringComparer.Ordinal)
			select item.RowKey).ToList();
		while (list2.Count < list.Count)
		{
			list2.Add(string.Empty);
		}
		return new ReportTable((branchScope == ReportBranchScope.ConsolidatedNetwork) ? "Sales Summary (network)" : "Sales Summary (General)", new _003C_003Ez__ReadOnlyArray<string>(new string[10] { "Date", "Invoice", "Patient / buyer", "Item Name", "Batch / B.No", "Expiry", "Qty", "Rate", "Total", "Branch" }), list, list2);
	}

	private async Task<ReportTable> CustomerPartyWiseAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, string? filter, CancellationToken token)
	{
		(DateTime, DateTime) utcBounds = GetUtcBounds(from, to);
		DateTime startUtc = utcBounds.Item1;
		DateTime endUtc = utcBounds.Item2;
		Guid? currentBranchId = branchService.CurrentBranchId;
		IQueryable<Sale> source = from sale in context.Sales.AsNoTracking()
			where !sale.IsDeleted && sale.SaleAtUtc >= startUtc && sale.SaleAtUtc < endUtc
			select sale;
		if (branchScope == ReportBranchScope.CurrentBranchOnly && currentBranchId.HasValue)
		{
			source = source.Where((Sale sale) => sale.BranchId == null || sale.BranchId == ((Guid?)currentBranchId).Value);
		}
		List<Sale> retail = await source.ToListAsync(token);
		IQueryable<WholesaleInvoice> source2 = from invoice in context.WholesaleInvoices.AsNoTracking()
			where invoice.Status == "Posted" && invoice.InvoiceAtUtc >= startUtc && invoice.InvoiceAtUtc < endUtc
			select invoice;
		if (branchScope == ReportBranchScope.CurrentBranchOnly && currentBranchId.HasValue)
		{
			source2 = source2.Where((WholesaleInvoice invoice) => invoice.BranchId == null || invoice.BranchId == ((Guid?)currentBranchId).Value);
		}
		List<WholesaleInvoice> wholesale = await source2.ToListAsync(token);
		Dictionary<Guid, Patient> patients = await context.Patients.AsNoTracking().ToDictionaryAsync((Patient item) => item.Id, token);
		Dictionary<Guid, Customer> customers = await context.Customers.AsNoTracking().ToDictionaryAsync((Customer item) => item.Id, token);
		(DateTime, string, string, IReadOnlyList<string>)[] source3 = (from entry in retail.Select((Sale sale) =>
			{
				Guid? patientId = sale.PatientId;
				object obj;
				if (patientId.HasValue)
				{
					Guid valueOrDefault = patientId.GetValueOrDefault();
					obj = patients.GetValueOrDefault(valueOrDefault);
				}
				else
				{
					obj = null;
				}
				Patient patient = (Patient)obj;
				string text = DisplayParty(patient?.Name);
				decimal value4 = Math.Max(0m, sale.TotalAmount - sale.PaidAmount);
				return ((DateTime At, string Key, string Party, IReadOnlyList<string> Row))(At: sale.SaleAtUtc, Key: $"R|{sale.Id}", Party: text, Row: new _003C_003Ez__ReadOnlyArray<string>(new string[8]
				{
					sale.SaleAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
					sale.InvoiceNo,
					text,
					patient?.Phone ?? string.Empty,
					"—",
					Format(sale.TotalAmount),
					Format(sale.PaidAmount),
					Format(value4)
				}));
			}).Concat(wholesale.Select((WholesaleInvoice invoice) =>
			{
				Customer valueOrDefault = customers.GetValueOrDefault(invoice.CustomerId);
				string text = DisplayParty(valueOrDefault?.Name);
				decimal value4 = Math.Max(0m, invoice.TotalAmount - invoice.PaidAmount);
				return ((DateTime At, string Key, string Party, IReadOnlyList<string> Row))(At: invoice.InvoiceAtUtc, Key: $"W|{invoice.Id}", Party: text, Row: new _003C_003Ez__ReadOnlyArray<string>(new string[8]
				{
					invoice.InvoiceAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
					invoice.InvoiceNo,
					text,
					valueOrDefault?.Phone ?? string.Empty,
					valueOrDefault?.Gstin ?? string.Empty,
					Format(invoice.TotalAmount),
					Format(invoice.PaidAmount),
					Format(value4)
				}));
			}))
			where string.IsNullOrEmpty(filter) || entry.Party.Contains(filter, StringComparison.OrdinalIgnoreCase)
			orderby entry.At
			select entry).ThenBy(((DateTime At, string Key, string Party, IReadOnlyList<string> Row) entry) => entry.Row[1], StringComparer.Ordinal).ToArray();
		List<IReadOnlyList<string>> list = source3.Select(((DateTime At, string Key, string Party, IReadOnlyList<string> Row) entry) => entry.Row).ToList();
		if (string.IsNullOrEmpty(filter) && list.Count > 0)
		{
			decimal value = source3.Sum(((DateTime At, string Key, string Party, IReadOnlyList<string> Row) entry) => decimal.Parse(entry.Row[5], CultureInfo.InvariantCulture));
			decimal value2 = source3.Sum(((DateTime At, string Key, string Party, IReadOnlyList<string> Row) entry) => decimal.Parse(entry.Row[6], CultureInfo.InvariantCulture));
			decimal value3 = source3.Sum(((DateTime At, string Key, string Party, IReadOnlyList<string> Row) entry) => decimal.Parse(entry.Row[7], CultureInfo.InvariantCulture));
			list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[8]
			{
				"",
				"",
				"TOTAL",
				"",
				"",
				Format(value),
				Format(value2),
				Format(value3)
			}));
		}
		List<string> list2 = source3.Select(((DateTime At, string Key, string Party, IReadOnlyList<string> Row) entry) => entry.Key).ToList();
		while (list2.Count < list.Count)
		{
			list2.Add(string.Empty);
		}
		return new ReportTable("Customer / Party-wise Sales", new _003C_003Ez__ReadOnlyArray<string>(new string[8] { "Date", "Invoice", "Customer Name", "Contact", "GSTIN", "Total Amount", "Paid", "Balance" }), list, list2);
	}

	private async Task<ReportTable> BatchWiseAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, string? filter, CancellationToken token)
	{
		List<SalesLineFact> source = (from item in await LoadSalesLineFactsAsync(@from, to, branchScope, token)
			where string.IsNullOrEmpty(filter) || item.BatchNo.Equals(filter, StringComparison.OrdinalIgnoreCase) || (filter == "(Unspecified)" && string.IsNullOrWhiteSpace(item.BatchNo))
			orderby item.AtUtc
			select item).ThenBy((SalesLineFact item) => item.BatchNo, StringComparer.OrdinalIgnoreCase).ToList();
		List<IReadOnlyList<string>> list = ((IEnumerable<SalesLineFact>)source).Select((Func<SalesLineFact, IReadOnlyList<string>>)((SalesLineFact item) => new _003C_003Ez__ReadOnlyArray<string>(new string[8]
		{
			item.AtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
			item.InvoiceNo,
			item.Item,
			item.BatchNo,
			item.Expiry,
			item.Quantity.ToString("0.##"),
			Format(item.UnitPrice),
			Format(item.LineTotal)
		}))).ToList();
		if (string.IsNullOrEmpty(filter) && list.Count > 0)
		{
			foreach (IGrouping<string, SalesLineFact> item in (from item in source
				group item by (!string.IsNullOrWhiteSpace(item.BatchNo)) ? item.BatchNo : "(Unspecified)").OrderBy((IGrouping<string, SalesLineFact> group) => group.Key, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[8]
				{
					"",
					"",
					"Subtotal — B.No " + item.Key,
					item.Key,
					"",
					item.Sum((SalesLineFact item) => item.Quantity).ToString("0.##"),
					"",
					Format(item.Sum((SalesLineFact item) => item.LineTotal))
				}));
			}
		}
		List<string> list2 = source.Select((SalesLineFact item) => item.RowKey).ToList();
		while (list2.Count < list.Count)
		{
			list2.Add(string.Empty);
		}
		return new ReportTable("Batch Number (B.No) wise Report", new _003C_003Ez__ReadOnlyArray<string>(new string[8] { "Date", "Invoice", "Item Name", "Batch No", "Expiry", "Qty", "Rate", "Total" }), list, list2);
	}

	private async Task<ReportTable> BrandProductWiseAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, string? filter, CancellationToken token)
	{
		List<SalesLineFact> source = (await LoadSalesLineFactsAsync(from, to, branchScope, token)).Where((SalesLineFact item) => string.IsNullOrEmpty(filter) || item.Brand.Equals(filter, StringComparison.OrdinalIgnoreCase) || (filter == "(Unspecified)" && item.Brand == "(Unspecified)")).ToList();
		List<IReadOnlyList<string>> list = (from row in source.GroupBy((SalesLineFact item) => item.Brand, StringComparer.OrdinalIgnoreCase).Select((Func<IGrouping<string, SalesLineFact>, IReadOnlyList<string>>)((IGrouping<string, SalesLineFact> @group) => new _003C_003Ez__ReadOnlyArray<string>(new string[5]
			{
				@group.Key,
				@group.Sum((SalesLineFact item) => item.Quantity).ToString("0.##"),
				Format(@group.Sum((SalesLineFact item) => item.LineTotal)),
				Format(@group.Sum((SalesLineFact item) => item.Taxable)),
				Format(@group.Sum((SalesLineFact item) => item.TaxAmount))
			})))
			orderby decimal.Parse(row[2], CultureInfo.InvariantCulture) descending
			select row).ToList();
		if (list.Count > 1)
		{
			list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[5]
			{
				"TOTAL",
				source.Sum((SalesLineFact item) => item.Quantity).ToString("0.##"),
				Format(source.Sum((SalesLineFact item) => item.LineTotal)),
				Format(source.Sum((SalesLineFact item) => item.Taxable)),
				Format(source.Sum((SalesLineFact item) => item.TaxAmount))
			}));
		}
		return new ReportTable("Brand / Product-wise Report", new _003C_003Ez__ReadOnlyArray<string>(new string[5] { "Brand / Product", "Total Units Sold", "Gross Turnover", "Taxable Value", "Tax Amount" }), list);
	}

	private async Task<ReportTable> ManufacturerWiseAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, string? filter, CancellationToken token)
	{
		List<SalesLineFact> source = (await LoadSalesLineFactsAsync(from, to, branchScope, token)).Where((SalesLineFact item) => string.IsNullOrEmpty(filter) || item.Manufacturer.Equals(filter, StringComparison.OrdinalIgnoreCase) || (filter == "(Unspecified)" && item.Manufacturer == "(Unspecified)")).ToList();
		List<IReadOnlyList<string>> list = (from row in source.GroupBy((SalesLineFact item) => item.Manufacturer, StringComparer.OrdinalIgnoreCase).Select((Func<IGrouping<string, SalesLineFact>, IReadOnlyList<string>>)((IGrouping<string, SalesLineFact> @group) => new _003C_003Ez__ReadOnlyArray<string>(new string[5]
			{
				@group.Key,
				@group.Sum((SalesLineFact item) => item.Quantity).ToString("0.##"),
				Format(@group.Sum((SalesLineFact item) => item.LineTotal)),
				Format(@group.Sum((SalesLineFact item) => item.Taxable)),
				Format(@group.Sum((SalesLineFact item) => item.TaxAmount))
			})))
			orderby decimal.Parse(row[2], CultureInfo.InvariantCulture) descending
			select row).ToList();
		if (list.Count > 1)
		{
			list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[5]
			{
				"TOTAL",
				source.Sum((SalesLineFact item) => item.Quantity).ToString("0.##"),
				Format(source.Sum((SalesLineFact item) => item.LineTotal)),
				Format(source.Sum((SalesLineFact item) => item.Taxable)),
				Format(source.Sum((SalesLineFact item) => item.TaxAmount))
			}));
		}
		return new ReportTable("Manufacturer / Company-wise Report", new _003C_003Ez__ReadOnlyArray<string>(new string[5] { "Manufacturer Name", "Total Units Sold", "Gross Turnover", "Taxable Value", "Tax Amount" }), list);
	}

	private async Task<ReportTable> SupplierWiseDetailAsync(DateOnly from, DateOnly to, string? filter, CancellationToken token)
	{
		List<PurchaseInvoice> invoices = await (from item in context.PurchaseInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceDate >= @from && item.InvoiceDate <= to
			select item).ToListAsync(token);
		Dictionary<Guid, Supplier> suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync((Supplier item) => item.Id, token);
		List<IReadOnlyList<string>> list = (from entry in (from entry in invoices.Select((PurchaseInvoice invoice) =>
				{
					Supplier valueOrDefault = suppliers.GetValueOrDefault(invoice.SupplierId);
					string text = valueOrDefault?.Name ?? "Unknown";
					return ((string Name, IReadOnlyList<string> Row))(Name: text, Row: new _003C_003Ez__ReadOnlyArray<string>(new string[8]
					{
						invoice.InvoiceDate.ToString("yyyy-MM-dd"),
						invoice.InvoiceNo,
						text,
						valueOrDefault?.Phone ?? string.Empty,
						valueOrDefault?.Gstin ?? string.Empty,
						Format(invoice.Subtotal),
						Format(invoice.TaxAmount),
						Format(invoice.TotalAmount)
					}));
				})
				where string.IsNullOrEmpty(filter) || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
				select entry).OrderBy(((string Name, IReadOnlyList<string> Row) entry) => entry.Row[0], StringComparer.Ordinal)
			select entry.Row).ToList();
		if (string.IsNullOrEmpty(filter) && list.Count > 0)
		{
			foreach (IGrouping<string, (string, PurchaseInvoice)> item in (from invoice in invoices
				select (Name: suppliers.GetValueOrDefault(invoice.SupplierId)?.Name ?? "Unknown", invoice: invoice) into entry
				where string.IsNullOrEmpty(filter) || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
				select entry).GroupBy(((string Name, PurchaseInvoice invoice) entry) => entry.Name, StringComparer.OrdinalIgnoreCase).OrderBy((IGrouping<string, (string Name, PurchaseInvoice invoice)> group) => group.Key, StringComparer.OrdinalIgnoreCase))
			{
				list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[8]
				{
					"",
					"",
					"Subtotal — " + item.Key,
					"",
					"",
					Format(item.Sum(((string Name, PurchaseInvoice invoice) item) => item.invoice.Subtotal)),
					Format(item.Sum(((string Name, PurchaseInvoice invoice) item) => item.invoice.TaxAmount)),
					Format(item.Sum(((string Name, PurchaseInvoice invoice) item) => item.invoice.TotalAmount))
				}));
			}
		}
		return new ReportTable("Purchaser / Supplier-wise Report", new _003C_003Ez__ReadOnlyArray<string>(new string[8] { "Date", "Invoice", "Supplier", "Contact", "GSTIN", "Taxable", "GST", "Total" }), list);
	}

	public async Task<ReportTable> GetSalesLineItemsAsync(DateOnly from, DateOnly to, CancellationToken token = default(CancellationToken))
	{
		if (to < from)
		{
			throw new ArgumentException("Report end date cannot precede its start date.");
		}
		(DateTime, DateTime) utcBounds = GetUtcBounds(from, to);
		DateTime startUtc = utcBounds.Item1;
		DateTime endUtc = utcBounds.Item2;
		List<Sale> retail = await (from sale in context.Sales.AsNoTracking()
			where sale.SaleAtUtc >= startUtc && sale.SaleAtUtc < endUtc
			select sale).ToListAsync(token);
		List<WholesaleInvoice> wholesale = await (from invoice in context.WholesaleInvoices.AsNoTracking()
			where invoice.Status == "Posted" && invoice.InvoiceAtUtc >= startUtc && invoice.InvoiceAtUtc < endUtc
			select invoice).ToListAsync(token);
		Dictionary<Guid, string> patients = await context.Patients.AsNoTracking().ToDictionaryAsync((Patient item) => item.Id, (Patient item) => item.Name, token);
		Dictionary<Guid, string> customers = await context.Customers.AsNoTracking().ToDictionaryAsync((Customer item) => item.Id, (Customer item) => item.Name, token);
		Dictionary<Guid, List<InvoiceDetailLine>> dictionary = await LoadSalesLinesAsync(retail, wholesale, token);
		IOrderedEnumerable<(DateTime At, Guid Id, string InvoiceNo, decimal TotalAmount, string Party)> orderedEnumerable = (from item in retail.Select((Sale sale) => (At: sale.SaleAtUtc, Id: sale.Id, InvoiceNo: sale.InvoiceNo, TotalAmount: sale.TotalAmount, Party: sale.PatientId.HasValue ? patients.GetValueOrDefault(sale.PatientId.Value, string.Empty) : string.Empty)).Concat(wholesale.Select((WholesaleInvoice invoice) => (At: invoice.InvoiceAtUtc, Id: invoice.Id, InvoiceNo: invoice.InvoiceNo, TotalAmount: invoice.TotalAmount, Party: customers.GetValueOrDefault(invoice.CustomerId, string.Empty))))
			orderby item.At
			select item).ThenBy(((DateTime At, Guid Id, string InvoiceNo, decimal TotalAmount, string Party) item) => item.InvoiceNo, StringComparer.Ordinal);
		List<IReadOnlyList<string>> list = new List<IReadOnlyList<string>>();
		foreach (var item in orderedEnumerable)
		{
			string text = item.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
			string text2 = DisplayParty(item.Party);
			List<InvoiceDetailLine> valueOrDefault = dictionary.GetValueOrDefault(item.Id);
			if (valueOrDefault == null || valueOrDefault.Count == 0)
			{
				list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[10]
				{
					text,
					item.InvoiceNo,
					text2,
					"(no line items)",
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					Format(item.TotalAmount)
				}));
				continue;
			}
			foreach (InvoiceDetailLine item2 in valueOrDefault)
			{
				list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[10]
				{
					text,
					item.InvoiceNo,
					text2,
					item2.Item,
					item2.BatchNo,
					item2.Expiry,
					item2.Quantity,
					Format(item2.UnitPrice),
					Format(item2.TaxRate),
					Format(item2.LineTotal)
				}));
			}
		}
		return new ReportTable("Sales line items", new _003C_003Ez__ReadOnlyArray<string>(new string[10] { "Date/time", "Invoice", "Patient / buyer", "Item", "Batch No", "Expiry (MM/YY)", "Quantity", "Unit price", "GST %", "Total amount" }), list);
	}

	public async Task<InvoiceDetail?> GetInvoiceDetailAsync(string rowKey, CancellationToken token = default(CancellationToken))
	{
		string[] array = rowKey.Split('|');
		if (array.Length != 2 || !Guid.TryParse(array[1], out var id))
		{
			return null;
		}
		if (array[0] == "R")
		{
			Sale sale = await context.Sales.AsNoTracking().FirstOrDefaultAsync((Sale item) => item.Id == id, token);
			if (sale == null)
			{
				return null;
			}
			Patient patient = ((!sale.PatientId.HasValue) ? null : (await context.Patients.AsNoTracking().FirstOrDefaultAsync((Patient item) => item.Id == sale.PatientId, token)));
			Patient patient2 = patient;
			Prescription prescription = ((!sale.PrescriptionId.HasValue) ? null : (await context.Prescriptions.AsNoTracking().FirstOrDefaultAsync((Prescription item) => item.Id == sale.PrescriptionId, token)));
			Prescription prescription2 = prescription;
			Dictionary<Guid, List<InvoiceDetailLine>> dictionary = await LoadSalesLinesAsync(new _003C_003Ez__ReadOnlySingleElementList<Sale>(sale), Array.Empty<WholesaleInvoice>(), token);
			return new InvoiceDetail("Retail bill", sale.InvoiceNo, sale.SaleAtUtc.ToLocalTime(), DisplayParty(patient2?.Name), patient2?.Phone ?? string.Empty, patient2?.Address ?? string.Empty, prescription2?.PrescriberName ?? string.Empty, prescription2?.PrescriberRegistrationNumber ?? string.Empty, prescription2?.ReferenceNumber ?? string.Empty, sale.PaymentStatus ?? string.Empty, sale.Subtotal, sale.TaxAmount, sale.DiscountAmount, sale.TotalAmount, sale.PaidAmount, sale.Notes ?? string.Empty, dictionary.GetValueOrDefault(sale.Id) ?? new List<InvoiceDetailLine>());
		}
		if (array[0] == "W")
		{
			WholesaleInvoice invoice = await context.WholesaleInvoices.AsNoTracking().FirstOrDefaultAsync((WholesaleInvoice item) => item.Id == id, token);
			if (invoice == null)
			{
				return null;
			}
			Customer customer = await context.Customers.AsNoTracking().FirstOrDefaultAsync((Customer item) => item.Id == invoice.CustomerId, token);
			Dictionary<Guid, List<InvoiceDetailLine>> dictionary2 = await LoadSalesLinesAsync(Array.Empty<Sale>(), new _003C_003Ez__ReadOnlySingleElementList<WholesaleInvoice>(invoice), token);
			return new InvoiceDetail("Wholesale invoice", invoice.InvoiceNo, invoice.InvoiceAtUtc.ToLocalTime(), DisplayParty(customer?.Name), customer?.Phone ?? string.Empty, customer?.Address ?? string.Empty, string.Empty, string.Empty, string.Empty, invoice.PaymentStatus ?? string.Empty, invoice.Subtotal, invoice.TaxAmount, invoice.DiscountAmount, invoice.TotalAmount, invoice.PaidAmount, invoice.Notes ?? string.Empty, dictionary2.GetValueOrDefault(invoice.Id) ?? new List<InvoiceDetailLine>());
		}
		return null;
	}

	private async Task<Dictionary<Guid, List<InvoiceDetailLine>>> LoadSalesLinesAsync(IReadOnlyCollection<Sale> retail, IReadOnlyCollection<WholesaleInvoice> wholesale, CancellationToken token)
	{
		Guid[] saleIds = retail.Select((Sale item) => item.Id).ToArray();
		Guid[] invoiceIds = wholesale.Select((WholesaleInvoice item) => item.Id).ToArray();
		List<SaleItem> saleItems = await (from item in context.SaleItems.AsNoTracking()
			where saleIds.Contains(item.SaleId)
			select item).ToListAsync(token);
		List<WholesaleInvoiceItem> invoiceItems = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where invoiceIds.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(token);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, token);
		Dictionary<Guid, Batch> batches = await context.Batches.AsNoTracking().ToDictionaryAsync((Batch item) => item.Id, token);
		Dictionary<Guid, List<InvoiceDetailLine>> dictionary = new Dictionary<Guid, List<InvoiceDetailLine>>();
		foreach (SaleItem item in saleItems.OrderBy((SaleItem item) => item.CreatedAtUtc))
		{
			if (!dictionary.TryGetValue(item.SaleId, out var value))
			{
				value = (dictionary[item.SaleId] = new List<InvoiceDetailLine>());
			}
			value.Add(Build(item.DrugId, item.BatchId, item.Quantity.ToString("0.##"), item.UnitPrice, item.TaxRate, item.DiscountAmount, item.LineTotal));
		}
		foreach (WholesaleInvoiceItem item2 in invoiceItems.OrderBy((WholesaleInvoiceItem item) => item.CreatedAtUtc))
		{
			if (!dictionary.TryGetValue(item2.WholesaleInvoiceId, out var value2))
			{
				value2 = (dictionary[item2.WholesaleInvoiceId] = new List<InvoiceDetailLine>());
			}
			string quantity = ((item2.FreeQuantity > 0m) ? $"{item2.Quantity:0.##} + {item2.FreeQuantity:0.##} free" : item2.Quantity.ToString("0.##"));
			value2.Add(Build(item2.DrugId, item2.BatchId, quantity, item2.UnitPrice, item2.TaxRate, item2.DiscountAmount, item2.LineTotal));
		}
		return dictionary;
		InvoiceDetailLine Build(Guid drugId, Guid batchId, string quantity2, decimal unitPrice, decimal taxRate, decimal discount, decimal lineTotal)
		{
			Drug valueOrDefault = drugs.GetValueOrDefault(drugId);
			Batch valueOrDefault2 = batches.GetValueOrDefault(batchId);
			return new InvoiceDetailLine(valueOrDefault?.Name ?? "Unknown", string.IsNullOrWhiteSpace(valueOrDefault?.GenericName) ? string.Empty : valueOrDefault.GenericName, valueOrDefault2?.BatchNo ?? string.Empty, FormatExpiry(valueOrDefault2?.ExpiryDate), quantity2, unitPrice, taxRate, discount, lineTotal);
		}
	}

	private static string ItemsSummary(List<InvoiceDetailLine>? lines)
	{
		if (lines != null && lines.Count != 0)
		{
			return string.Join("; ", lines.Select((InvoiceDetailLine line) => (!string.IsNullOrEmpty(line.Composition)) ? $"{line.Item} ({line.Composition}) x{line.Quantity}" : (line.Item + " x" + line.Quantity)));
		}
		return string.Empty;
	}

	private static string DisplayParty(string? name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			return name;
		}
		return "Walk-in Customer";
	}

	public static string FormatExpiry(DateOnly? expiry)
	{
		return expiry?.ToString("MM/yy") ?? string.Empty;
	}

	private async Task<ReportTable> PurchasesAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, CancellationToken token)
	{
		Guid? currentBranchId = branchService.CurrentBranchId;
		IQueryable<PurchaseInvoice> source = from item in context.PurchaseInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceDate >= @from && item.InvoiceDate <= to
			select item;
		if (branchScope == ReportBranchScope.CurrentBranchOnly && currentBranchId.HasValue)
		{
			source = source.Where((PurchaseInvoice item) => item.BranchId == null || item.BranchId == ((Guid?)currentBranchId).Value);
		}
		List<PurchaseInvoice> invoices = await source.ToListAsync(token);
		Dictionary<Guid, string> suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync((Supplier item) => item.Id, (Supplier item) => item.Name, token);
		string currentCode = branchService.CurrentSettings.CurrentBranchCode;
		List<IReadOnlyList<string>> list = ((IEnumerable<PurchaseInvoice>)invoices.OrderBy((PurchaseInvoice item) => item.InvoiceDate)).Select((Func<PurchaseInvoice, IReadOnlyList<string>>)((PurchaseInvoice invoice) => new _003C_003Ez__ReadOnlyArray<string>(new string[7]
		{
			invoice.InvoiceDate.ToString("yyyy-MM-dd"),
			invoice.InvoiceNo,
			suppliers.GetValueOrDefault(invoice.SupplierId, string.Empty),
			Format(invoice.Subtotal),
			Format(invoice.TaxAmount),
			Format(invoice.TotalAmount),
			currentCode
		}))).ToList();
		if (branchScope == ReportBranchScope.ConsolidatedNetwork)
		{
			foreach (BranchSalesSummaryDocument item in branchService.LoadPeerSalesSummaries())
			{
				list.Add(new _003C_003Ez__ReadOnlyArray<string>(new string[7]
				{
					item.ToDate?.ToString("yyyy-MM-dd") ?? item.GeneratedAtUtc.ToLocalTime().ToString("yyyy-MM-dd"),
					item.BranchCode + " network",
					item.BranchName,
					"—",
					"—",
					Format(item.SalesTotal),
					item.BranchCode
				}));
			}
		}
		return new ReportTable((branchScope == ReportBranchScope.ConsolidatedNetwork) ? "Purchases / network" : "Purchases", new _003C_003Ez__ReadOnlyArray<string>(new string[7] { "Date", "Invoice", "Supplier", "Subtotal", "GST", "Total", "Branch" }), list);
	}

	private async Task<ReportTable> ProfitAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		(DateTime, DateTime) utcBounds = GetUtcBounds(from, to);
		DateTime startUtc = utcBounds.Item1;
		DateTime endUtc = utcBounds.Item2;
		List<Sale> retailSales = await (from item in context.Sales.AsNoTracking()
			where item.SaleAtUtc >= startUtc && item.SaleAtUtc < endUtc
			select item).ToListAsync(token);
		List<WholesaleInvoice> source = await (from item in context.WholesaleInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceAtUtc >= startUtc && item.InvoiceAtUtc < endUtc
			select item).ToListAsync(token);
		Guid[] retailIds = retailSales.Select((Sale item) => item.Id).ToArray();
		Guid[] wholesaleIds = source.Select((WholesaleInvoice item) => item.Id).ToArray();
		List<SaleItem> retailItems = await (from item in context.SaleItems.AsNoTracking()
			where retailIds.Contains(item.SaleId)
			select item).ToListAsync(token);
		List<WholesaleInvoiceItem> wholesaleItems = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where wholesaleIds.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(token);
		Dictionary<Guid, string> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, (Drug item) => item.Name, token);
		Dictionary<Guid, Batch> batches = await context.Batches.AsNoTracking().ToDictionaryAsync((Batch item) => item.Id, token);
		IReadOnlyList<string>[] rows = (from row in (from item in retailItems.Select((SaleItem item) => (DrugId: item.DrugId, Qty: item.Quantity, Revenue: item.LineTotal, Cost: batches[item.BatchId].PurchasePrice * item.Quantity)).Concat(wholesaleItems.Select((WholesaleInvoiceItem item) => (DrugId: item.DrugId, Qty: item.Quantity + item.FreeQuantity, Revenue: item.LineTotal, Cost: batches[item.BatchId].PurchasePrice * (item.Quantity + item.FreeQuantity))))
				group item by item.DrugId).Select((Func<IGrouping<Guid, (Guid, decimal, decimal, decimal)>, IReadOnlyList<string>>)((IGrouping<Guid, (Guid DrugId, decimal Qty, decimal Revenue, decimal Cost)> @group) =>
			{
				decimal num = @group.Sum(((Guid DrugId, decimal Qty, decimal Revenue, decimal Cost) item) => item.Revenue);
				decimal num2 = @group.Sum(((Guid DrugId, decimal Qty, decimal Revenue, decimal Cost) item) => item.Cost);
				return new _003C_003Ez__ReadOnlyArray<string>(new string[5]
				{
					drugs.GetValueOrDefault(@group.Key, "Unknown"),
					@group.Sum(((Guid DrugId, decimal Qty, decimal Revenue, decimal Cost) item) => item.Qty).ToString("0.##"),
					Format(num),
					Format(num2),
					Format(num - num2)
				});
			}))
			orderby decimal.Parse(row[4], CultureInfo.InvariantCulture) descending
			select row).ToArray();
		return new ReportTable("Profit by drug (estimate at recorded purchase rate)", new _003C_003Ez__ReadOnlyArray<string>(new string[5] { "Drug", "Qty", "Sales", "Purchase cost", "Gross margin" }), rows);
	}

	private async Task<ReportTable> GstAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		(DateTime, DateTime) utcBounds = GetUtcBounds(from, to);
		DateTime startUtc = utcBounds.Item1;
		DateTime endUtc = utcBounds.Item2;
		Guid[] invoiceIds = (await (from item in context.WholesaleInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceAtUtc >= startUtc && item.InvoiceAtUtc < endUtc
			select item).ToListAsync(token)).Select((WholesaleInvoice item) => item.Id).ToArray();
		List<WholesaleInvoiceItem> wholesaleItems = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where invoiceIds.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(token);
		Guid[] retailIds = await (from item in context.Sales.AsNoTracking()
			where item.SaleAtUtc >= startUtc && item.SaleAtUtc < endUtc
			select item.Id).ToArrayAsync(token);
		List<SaleItem> retailItems = await (from item in context.SaleItems.AsNoTracking()
			where retailIds.Contains(item.SaleId)
			select item).ToListAsync(token);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, token);
		var first = from item in wholesaleItems
			group item by (Hsn: drugs[item.DrugId].HsnCode ?? string.Empty, TaxRate: item.TaxRate) into @group
			select new
			{
				Hsn = @group.Key.Hsn,
				TaxRate = @group.Key.TaxRate,
				Taxable = @group.Sum((WholesaleInvoiceItem item) => item.TaxableAmount),
				Cgst = @group.Sum((WholesaleInvoiceItem item) => item.CgstAmount),
				Sgst = @group.Sum((WholesaleInvoiceItem item) => item.SgstAmount),
				Igst = @group.Sum((WholesaleInvoiceItem item) => item.IgstAmount),
				Total = @group.Sum((WholesaleInvoiceItem item) => item.LineTotal)
			};
		var second = (from item in retailItems
			group item by (Hsn: drugs[item.DrugId].HsnCode ?? string.Empty, TaxRate: item.TaxRate)).Select((IGrouping<(string Hsn, decimal TaxRate), SaleItem> group) =>
		{
			decimal num = group.Sum((SaleItem item) => item.LineTotal);
			decimal num2 = decimal.Round(num / (1m + group.Key.TaxRate / 100m), 2, MidpointRounding.AwayFromZero);
			decimal num3 = num - num2;
			decimal num4 = decimal.Round(num3 / 2m, 2, MidpointRounding.AwayFromZero);
			return new
			{
				Hsn = group.Key.Hsn,
				TaxRate = group.Key.TaxRate,
				Taxable = num2,
				Cgst = num4,
				Sgst = num3 - num4,
				Igst = 0m,
				Total = num
			};
		});
		IReadOnlyList<string>[] rows = (from item in first.Concat(second)
			group item by (Hsn: item.Hsn, TaxRate: item.TaxRate) into @group
			select (IReadOnlyList<string>)new _003C_003Ez__ReadOnlyArray<string>(new string[7]
			{
				@group.Key.Hsn,
				Format(@group.Key.TaxRate),
				Format(@group.Sum(item => item.Taxable)),
				Format(@group.Sum(item => item.Cgst)),
				Format(@group.Sum(item => item.Sgst)),
				Format(@group.Sum(item => item.Igst)),
				Format(@group.Sum(item => item.Total))
			})).ToArray();
		return new ReportTable("Tax & GST Summary", new _003C_003Ez__ReadOnlyArray<string>(new string[7] { "HSN", "Rate %", "Taxable", "CGST", "SGST", "IGST", "Total" }), rows);
	}

	private async Task<ReportTable> TopSellingAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		(DateTime, DateTime) utcBounds = GetUtcBounds(from, to);
		DateTime startUtc = utcBounds.Item1;
		DateTime endUtc = utcBounds.Item2;
		Guid[] sales = await (from item in context.Sales.AsNoTracking()
			where item.SaleAtUtc >= startUtc && item.SaleAtUtc < endUtc
			select item.Id).ToArrayAsync(token);
		Guid[] wholesale = await (from item in context.WholesaleInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceAtUtc >= startUtc && item.InvoiceAtUtc < endUtc
			select item.Id).ToArrayAsync(token);
		List<SaleItem> retailItems = await (from item in context.SaleItems.AsNoTracking()
			where sales.Contains(item.SaleId)
			select item).ToListAsync(token);
		List<WholesaleInvoiceItem> wholesaleItems = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where wholesale.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(token);
		Dictionary<Guid, string> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, (Drug item) => item.Name, token);
		IReadOnlyList<string>[] rows = (from row in (from item in retailItems.Select((SaleItem item) => (DrugId: item.DrugId, Quantity: item.Quantity, LineTotal: item.LineTotal)).Concat(wholesaleItems.Select((WholesaleInvoiceItem item) => (DrugId: item.DrugId, Quantity: item.Quantity + item.FreeQuantity, LineTotal: item.LineTotal)))
				group item by item.DrugId).Select((Func<IGrouping<Guid, (Guid, decimal, decimal)>, IReadOnlyList<string>>)((IGrouping<Guid, (Guid DrugId, decimal Quantity, decimal LineTotal)> @group) => new _003C_003Ez__ReadOnlyArray<string>(new string[3]
			{
				drugs.GetValueOrDefault(@group.Key, "Unknown"),
				@group.Sum(((Guid DrugId, decimal Quantity, decimal LineTotal) item) => item.Quantity).ToString("0.##"),
				Format(@group.Sum(((Guid DrugId, decimal Quantity, decimal LineTotal) item) => item.LineTotal))
			})))
			orderby decimal.Parse(row[1], CultureInfo.InvariantCulture) descending
			select row).ToArray();
		return new ReportTable("Top-selling drugs", new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "Drug", "Quantity", "Sales" }), rows);
	}

	private async Task<ReportTable> ExpiryAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		List<Batch> batches = await (from item in context.Batches.AsNoTracking()
			where item.ExpiryDate.HasValue && item.ExpiryDate.Value >= @from && item.ExpiryDate.Value <= to
			select item).ToListAsync(token);
		Guid[] ids = batches.Select((Batch item) => item.Id).ToArray();
		Dictionary<Guid, decimal> stock = await (from item in context.StockMovements.AsNoTracking()
			where ids.Contains(item.BatchId)
			group item by item.BatchId into @group
			select new
			{
				Id = @group.Key,
				Qty = @group.Sum((StockMovement item) => item.QuantityChange)
			}).ToDictionaryAsync(item => item.Id, item => item.Qty, token);
		Dictionary<Guid, string> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, (Drug item) => item.Name, token);
		IReadOnlyList<string>[] rows = ((IEnumerable<(Batch, decimal)>)(from batch in batches
			select (Batch: batch, Qty: stock.GetValueOrDefault(batch.Id)) into item
			where item.Qty > 0m
			orderby item.Batch.ExpiryDate
			select item)).Select((Func<(Batch, decimal), IReadOnlyList<string>>)(((Batch Batch, decimal Qty) item) => new _003C_003Ez__ReadOnlyArray<string>(new string[5]
		{
			drugs.GetValueOrDefault(item.Batch.DrugId, "Unknown"),
			item.Batch.BatchNo,
			item.Batch.ExpiryDate.Value.ToString("yyyy-MM-dd"),
			item.Qty.ToString("0.##"),
			Format(item.Batch.Mrp.GetValueOrDefault())
		}))).ToArray();
		return new ReportTable("Expiry report", new _003C_003Ez__ReadOnlyArray<string>(new string[5] { "Drug", "Batch", "Expiry", "Stock", "MRP" }), rows);
	}

	private async Task<ReportTable> LowStockAsync(CancellationToken token)
	{
		List<Drug> drugs = await (from item in context.Drugs.AsNoTracking()
			where item.IsActive && item.ReorderLevel > 0m
			select item).ToListAsync(token);
		Guid[] ids = drugs.Select((Drug item) => item.Id).ToArray();
		List<Batch> batches = await (from item in context.Batches.AsNoTracking()
			where ids.Contains(item.DrugId)
			select item).ToListAsync(token);
		Guid[] batchIds = batches.Select((Batch item) => item.Id).ToArray();
		Dictionary<Guid, decimal> movements = await (from item in context.StockMovements.AsNoTracking()
			where batchIds.Contains(item.BatchId)
			group item by item.BatchId into @group
			select new
			{
				Id = @group.Key,
				Qty = @group.Sum((StockMovement item) => item.QuantityChange)
			}).ToDictionaryAsync(item => item.Id, item => item.Qty, token);
		IReadOnlyList<string>[] rows = ((IEnumerable<(Drug, decimal)>)(from item in drugs.Select((Drug drug) =>
			{
				decimal item = batches.Where((Batch batch) => batch.DrugId == drug.Id).Sum((Batch batch) => movements.GetValueOrDefault(batch.Id));
				return (Drug: drug, Current: item);
			})
			where StockShortage.IsShortage(item.Current, item.Drug.ReorderLevel)
			orderby item.Current / item.Drug.ReorderLevel
			select item)).Select((Func<(Drug, decimal), IReadOnlyList<string>>)(((Drug Drug, decimal Current) item) => new _003C_003Ez__ReadOnlyArray<string>(new string[4]
		{
			item.Drug.Name,
			item.Current.ToString("0.##"),
			item.Drug.ReorderLevel.ToString("0.##"),
			StockShortage.SuggestedOrderQuantity(item.Current, item.Drug.ReorderLevel).ToString("0.##")
		}))).ToArray();
		return new ReportTable("Low-stock report", new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "Drug", "On hand", "Reorder level", "Suggested order" }), rows);
	}

	private async Task<ReportTable> SupplierWiseAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		return await SupplierWiseDetailAsync(from, to, null, token);
	}

	private async Task<List<SalesLineFact>> LoadSalesLineFactsAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, CancellationToken token)
	{
		(DateTime, DateTime) utcBounds = GetUtcBounds(from, to);
		DateTime startUtc = utcBounds.Item1;
		DateTime endUtc = utcBounds.Item2;
		Guid? currentBranchId = branchService.CurrentBranchId;
		string branchCode = branchService.CurrentSettings.CurrentBranchCode;
		IQueryable<Sale> source = from sale2 in context.Sales.AsNoTracking()
			where !sale2.IsDeleted && sale2.SaleAtUtc >= startUtc && sale2.SaleAtUtc < endUtc
			select sale2;
		if (branchScope == ReportBranchScope.CurrentBranchOnly && currentBranchId.HasValue)
		{
			source = source.Where((Sale sale2) => sale2.BranchId == null || sale2.BranchId == ((Guid?)currentBranchId).Value);
		}
		List<Sale> retail = await source.ToListAsync(token);
		IQueryable<WholesaleInvoice> source2 = from wholesaleInvoice in context.WholesaleInvoices.AsNoTracking()
			where wholesaleInvoice.Status == "Posted" && wholesaleInvoice.InvoiceAtUtc >= startUtc && wholesaleInvoice.InvoiceAtUtc < endUtc
			select wholesaleInvoice;
		if (branchScope == ReportBranchScope.CurrentBranchOnly && currentBranchId.HasValue)
		{
			source2 = source2.Where((WholesaleInvoice wholesaleInvoice) => wholesaleInvoice.BranchId == null || wholesaleInvoice.BranchId == ((Guid?)currentBranchId).Value);
		}
		List<WholesaleInvoice> wholesale = await source2.ToListAsync(token);
		Dictionary<Guid, Patient> patients = await context.Patients.AsNoTracking().ToDictionaryAsync((Patient item) => item.Id, token);
		Dictionary<Guid, Customer> customers = await context.Customers.AsNoTracking().ToDictionaryAsync((Customer item) => item.Id, token);
		Guid[] saleIds = retail.Select((Sale item) => item.Id).ToArray();
		Guid[] invoiceIds = wholesale.Select((WholesaleInvoice item) => item.Id).ToArray();
		List<SaleItem> saleItems = await (from item in context.SaleItems.AsNoTracking()
			where saleIds.Contains(item.SaleId)
			select item).ToListAsync(token);
		List<WholesaleInvoiceItem> invoiceItems = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			where invoiceIds.Contains(item.WholesaleInvoiceId)
			select item).ToListAsync(token);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync((Drug item) => item.Id, token);
		Dictionary<Guid, Batch> batches = await context.Batches.AsNoTracking().ToDictionaryAsync((Batch item) => item.Id, token);
		Guid[] catalogIds = (from item in drugs.Values
			where item.CatalogMedicineId.HasValue
			select item.CatalogMedicineId.Value).Distinct().ToArray();
		Dictionary<Guid, CatalogMedicine> catalogs = await (from item in context.CatalogMedicines.AsNoTracking()
			where catalogIds.Contains(item.Id)
			select item).ToDictionaryAsync((CatalogMedicine item) => item.Id, token);
		List<SalesLineFact> list = new List<SalesLineFact>();
		foreach (Sale sale in retail)
		{
			Guid? patientId = sale.PatientId;
			object obj;
			if (patientId.HasValue)
			{
				Guid valueOrDefault = patientId.GetValueOrDefault();
				obj = patients.GetValueOrDefault(valueOrDefault);
			}
			else
			{
				obj = null;
			}
			Patient patient = (Patient)obj;
			string party = DisplayParty(patient?.Name);
			List<SaleItem> list2 = saleItems.Where((SaleItem item) => item.SaleId == sale.Id).ToList();
			if (list2.Count == 0)
			{
				list.Add(new SalesLineFact(sale.Id, $"R|{sale.Id}", sale.SaleAtUtc, sale.InvoiceNo, party, patient?.Phone ?? string.Empty, "—", "(no line items)", "(Unspecified)", "(Unspecified)", string.Empty, string.Empty, 0m, 0m, 0m, 0m, sale.TotalAmount, branchCode));
				continue;
			}
			foreach (SaleItem item in list2)
			{
				Drug valueOrDefault2 = drugs.GetValueOrDefault(item.DrugId);
				Batch valueOrDefault3 = batches.GetValueOrDefault(item.BatchId);
				decimal num = ((item.TaxRate <= 0m) ? item.LineTotal : decimal.Round(item.LineTotal / (1m + item.TaxRate / 100m), 2, MidpointRounding.AwayFromZero));
				list.Add(new SalesLineFact(sale.Id, $"R|{sale.Id}", sale.SaleAtUtc, sale.InvoiceNo, party, patient?.Phone ?? string.Empty, "—", valueOrDefault2?.Name ?? "Unknown", BrandOf(valueOrDefault2), ManufacturerOf(valueOrDefault2), valueOrDefault3?.BatchNo ?? string.Empty, FormatExpiry(valueOrDefault3?.ExpiryDate), item.Quantity, item.UnitPrice, num, item.LineTotal - num, item.LineTotal, branchCode));
			}
		}
		foreach (WholesaleInvoice invoice in wholesale)
		{
			Customer valueOrDefault4 = customers.GetValueOrDefault(invoice.CustomerId);
			string party2 = DisplayParty(valueOrDefault4?.Name);
			List<WholesaleInvoiceItem> list3 = invoiceItems.Where((WholesaleInvoiceItem item) => item.WholesaleInvoiceId == invoice.Id).ToList();
			if (list3.Count == 0)
			{
				list.Add(new SalesLineFact(invoice.Id, $"W|{invoice.Id}", invoice.InvoiceAtUtc, invoice.InvoiceNo, party2, valueOrDefault4?.Phone ?? string.Empty, valueOrDefault4?.Gstin ?? string.Empty, "(no line items)", "(Unspecified)", "(Unspecified)", string.Empty, string.Empty, 0m, 0m, 0m, 0m, invoice.TotalAmount, branchCode));
				continue;
			}
			foreach (WholesaleInvoiceItem item2 in list3)
			{
				Drug valueOrDefault5 = drugs.GetValueOrDefault(item2.DrugId);
				Batch valueOrDefault6 = batches.GetValueOrDefault(item2.BatchId);
				decimal quantity = item2.Quantity + item2.FreeQuantity;
				list.Add(new SalesLineFact(invoice.Id, $"W|{invoice.Id}", invoice.InvoiceAtUtc, invoice.InvoiceNo, party2, valueOrDefault4?.Phone ?? string.Empty, valueOrDefault4?.Gstin ?? string.Empty, valueOrDefault5?.Name ?? "Unknown", BrandOf(valueOrDefault5), ManufacturerOf(valueOrDefault5), valueOrDefault6?.BatchNo ?? string.Empty, FormatExpiry(valueOrDefault6?.ExpiryDate), quantity, item2.UnitPrice, item2.TaxableAmount, item2.CgstAmount + item2.SgstAmount + item2.IgstAmount, item2.LineTotal, branchCode));
			}
		}
		return list;
		string BrandOf(Drug? drug)
		{
			if (drug == null)
			{
				return "(Unspecified)";
			}
			if (!string.IsNullOrWhiteSpace(drug.BrandName))
			{
				return drug.BrandName.Trim();
			}
			Guid? catalogMedicineId = drug.CatalogMedicineId;
			if (catalogMedicineId.HasValue)
			{
				Guid valueOrDefault7 = catalogMedicineId.GetValueOrDefault();
				if (catalogs.TryGetValue(valueOrDefault7, out var value) && !string.IsNullOrWhiteSpace(value.BrandName))
				{
					return value.BrandName.Trim();
				}
			}
			if (!string.IsNullOrWhiteSpace(drug.Name))
			{
				return drug.Name.Trim();
			}
			return "(Unspecified)";
		}
		string ManufacturerOf(Drug? drug)
		{
			Guid? guid = drug?.CatalogMedicineId;
			if (guid.HasValue)
			{
				Guid valueOrDefault7 = guid.GetValueOrDefault();
				if (catalogs.TryGetValue(valueOrDefault7, out var value) && !string.IsNullOrWhiteSpace(value.Manufacturer))
				{
					return value.Manufacturer.Trim();
				}
			}
			return "(Unspecified)";
		}
	}

	private async Task<IEnumerable<string>> LoadSupplierNamesAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		List<Guid> invoices = await (from item in context.PurchaseInvoices.AsNoTracking()
			where item.Status == "Posted" && item.InvoiceDate >= @from && item.InvoiceDate <= to
			select item.SupplierId).Distinct().ToListAsync(token);
		return await (from item in context.Suppliers.AsNoTracking()
			where invoices.Contains(item.Id)
			select item.Name).ToListAsync(token);
	}

	private static string? NormalizeFilter(string? dimensionFilter)
	{
		if (string.IsNullOrWhiteSpace(dimensionFilter) || dimensionFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return dimensionFilter.Trim();
	}

	private async Task<decimal> GetWholesaleOutstandingAsync(DateOnly asOf, CancellationToken token)
	{
		List<WholesaleInvoice> invoices = await (from item in context.WholesaleInvoices.AsNoTracking()
			where item.Status == "Posted"
			select item).ToListAsync(token);
		Dictionary<Guid, decimal> receipts = await (from item in context.Receipts.AsNoTracking()
			where item.WholesaleInvoiceId.HasValue
			group item by item.WholesaleInvoiceId.Value into @group
			select new
			{
				Id = @group.Key,
				Amount = @group.Sum((Receipt item) => item.Amount)
			}).ToDictionaryAsync(item => item.Id, item => item.Amount, token);
		List<CustomerLedgerEntry> credits = await (from item in context.CustomerLedgerEntries.AsNoTracking()
			where item.EntryType.Contains("CreditNote")
			select item).ToListAsync(token);
		return invoices.Where((WholesaleInvoice item) => DateOnly.FromDateTime(item.InvoiceAtUtc.ToLocalTime()) <= asOf).Sum((WholesaleInvoice invoice) =>
		{
			decimal num = credits.Where((CustomerLedgerEntry entry) => entry.CustomerId == invoice.CustomerId && (entry.Notes?.Contains(invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase) ?? false)).Sum((CustomerLedgerEntry entry) => entry.Credit);
			return Math.Max(0m, invoice.TotalAmount - Math.Max(invoice.PaidAmount, receipts.GetValueOrDefault(invoice.Id)) - num);
		});
	}

	private static (DateTime StartUtc, DateTime EndUtc) GetUtcBounds(DateOnly from, DateOnly to)
	{
		return (StartUtc: from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime(), EndUtc: to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime());
	}

	private static IReadOnlyList<string> Row(string date, string invoice, string party, string items, decimal total, decimal paid, decimal tax, string branchCode = "")
	{
		return new _003C_003Ez__ReadOnlyArray<string>(new string[8]
		{
			date,
			invoice,
			DisplayParty(party),
			items,
			Format(total),
			Format(paid),
			Format(tax),
			branchCode
		});
	}

	private static string Format(decimal value)
	{
		return value.ToString("0.00", CultureInfo.InvariantCulture);
	}
}
