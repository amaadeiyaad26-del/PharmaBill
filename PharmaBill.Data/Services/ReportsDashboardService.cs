using PharmaBill.Core;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class ReportsDashboardService(PharmaBillDbContext context, BranchService branchService, SalesAnalysisService salesAnalysis)
{
	private sealed record SalesLineFact(Guid DocumentId, string RowKey, DateTime AtUtc, string InvoiceNo, string Party, string Contact, string Gstin, string Item, string Brand, string Manufacturer, string BatchNo, string Expiry, decimal Quantity, decimal UnitPrice, decimal Taxable, decimal TaxAmount, decimal LineTotal, string BranchCode);

	public const int TrendDays = 7;

	public const int FirstBusinessHour = 9;

	public const int LastBusinessHour = 22;

	public const int PeakHoursLookbackDays = 30;

	public async Task<DashboardAnalytics> GetDashboardAnalyticsAsync(DateTime nowLocal, CancellationToken cancellationToken = default(CancellationToken))
	{
		DateOnly dateOnly = DateOnly.FromDateTime(nowLocal);
		DateTime tomorrowUtc = dateOnly.AddDays(1).StartOfDayUtc();
		DateOnly monthStart = new DateOnly(dateOnly.Year, dateOnly.Month, 1);
		DateOnly trendStart = dateOnly.AddDays(-6);
		DateOnly lookbackStart = dateOnly.AddDays(-29);
		DateTime earliestUtc = new DateOnly[3] { monthStart, trendStart, lookbackStart }.Min().StartOfDayUtc();
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
		List<(Guid Id, DateTime Local, decimal TotalAmount)> documents = retail.Select(item => (Id: item.Id, Local: item.At.ToBusinessLocal(), TotalAmount: item.TotalAmount)).Concat(source.Select(item => (Id: item.Id, Local: item.At.ToBusinessLocal(), TotalAmount: item.TotalAmount))).ToList();
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
		DateTime todayStartUtc = today.StartOfDayUtc();
		DateTime tomorrowUtc = today.AddDays(1).StartOfDayUtc();
		DateTime monthStartUtc = new DateOnly(today.Year, today.Month, 1).StartOfDayUtc();
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
		case ReportKind.DailyMis:
			result = await DailyMisAsync(from, to, cancellationToken);
			break;
		case ReportKind.AbcAnalysis:
			result = await AbcAnalysisAsync(from, to, cancellationToken);
			break;
		case ReportKind.SaleBookDaily:
			result = await SaleBookAsync(from, to, branchScope, monthly: false, cancellationToken);
			break;
		case ReportKind.SaleBookMonthly:
			result = await SaleBookAsync(from, to, branchScope, monthly: true, cancellationToken);
			break;
		case ReportKind.PrescriberWise:
			result = await PrescriberWiseAsync(from, to, cancellationToken);
			break;
		case ReportKind.PurchaseReturnBook:
			result = await PurchaseReturnBookAsync(from, to, cancellationToken);
			break;
		case ReportKind.DayBook:
			result = await DayBookAsync(from, to, cancellationToken);
			break;
		case ReportKind.PartyBalances:
			result = await PartyBalancesAsync(to, cancellationToken);
			break;
		case ReportKind.ExpiryClaimsAgeing:
			result = await ExpiryClaimQueries.PendingAgeingAsync(context, cancellationToken);
			break;
		case ReportKind.ExpiryClaimRecovery:
			result = await ExpiryClaimQueries.RecoveryAsync(context, from, to, cancellationToken);
			break;
		case ReportKind.SaleReturnBook:
			result = await SaleReturnBookAsync(from, to, cancellationToken);
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
			item.AtUtc.ToBusinessLocal().ToString("yyyy-MM-dd HH:mm"),
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
		// Other branches' summaries are left out until they can be exported for this report's own dates;
		// the cached ones cover a different period and are never refreshed (docs/BRANCH_SUMMARIES.md).
		List<string> list2 = (from item in source.OrderBy((SalesLineFact item) => item.AtUtc).ThenBy((SalesLineFact item) => item.InvoiceNo, StringComparer.Ordinal)
			select item.RowKey).ToList();
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
					sale.SaleAtUtc.ToBusinessLocal().ToString("yyyy-MM-dd HH:mm"),
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
					invoice.InvoiceAtUtc.ToBusinessLocal().ToString("yyyy-MM-dd HH:mm"),
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
			item.AtUtc.ToBusinessLocal().ToString("yyyy-MM-dd HH:mm"),
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
			where (item.Status == PurchaseInvoice.PostedStatus || item.Status == PurchaseInvoice.CommittedStatus) && item.InvoiceDate >= @from && item.InvoiceDate <= to
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
			string text = item.At.ToBusinessLocal().ToString("yyyy-MM-dd HH:mm");
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
			BuyerSnapshot? recordedPatient = TaxDocumentSnapshot.Deserialize<BuyerSnapshot>(sale.BuyerSnapshotJson);
			if (recordedPatient != null)
			{
				return new InvoiceDetail("Retail bill", sale.InvoiceNo, sale.SaleAtUtc.ToBusinessLocal(), DisplayParty(recordedPatient.Name), recordedPatient.Phone ?? string.Empty, recordedPatient.Address ?? string.Empty, recordedPatient.PrescriberName ?? string.Empty, recordedPatient.PrescriberRegistrationNumber ?? string.Empty, recordedPatient.PrescriptionReference ?? string.Empty, sale.PaymentStatus ?? string.Empty, sale.Subtotal, sale.TaxAmount, sale.DiscountAmount, sale.TotalAmount, sale.PaidAmount, sale.Notes ?? string.Empty, dictionary.GetValueOrDefault(sale.Id) ?? new List<InvoiceDetailLine>(), sale.Id, IsWholesale: false);
			}
			return new InvoiceDetail("Retail bill", sale.InvoiceNo, sale.SaleAtUtc.ToBusinessLocal(), DisplayParty(patient2?.Name), patient2?.Phone ?? string.Empty, patient2?.Address ?? string.Empty, prescription2?.PrescriberName ?? string.Empty, prescription2?.PrescriberRegistrationNumber ?? string.Empty, prescription2?.ReferenceNumber ?? string.Empty, sale.PaymentStatus ?? string.Empty, sale.Subtotal, sale.TaxAmount, sale.DiscountAmount, sale.TotalAmount, sale.PaidAmount, sale.Notes ?? string.Empty, dictionary.GetValueOrDefault(sale.Id) ?? new List<InvoiceDetailLine>(), sale.Id, IsWholesale: false);
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
			BuyerSnapshot? recordedBuyer = TaxDocumentSnapshot.Deserialize<BuyerSnapshot>(invoice.BuyerSnapshotJson);
			if (recordedBuyer != null)
			{
				return new InvoiceDetail("Wholesale invoice", invoice.InvoiceNo, invoice.InvoiceAtUtc.ToBusinessLocal(), DisplayParty(invoice.BuyerLegalName ?? recordedBuyer.Name), recordedBuyer.Phone ?? string.Empty, invoice.BuyerAddress ?? string.Empty, string.Empty, string.Empty, string.Empty, invoice.PaymentStatus ?? string.Empty, invoice.Subtotal, invoice.TaxAmount, invoice.DiscountAmount, invoice.TotalAmount, invoice.PaidAmount, invoice.Notes ?? string.Empty, dictionary2.GetValueOrDefault(invoice.Id) ?? new List<InvoiceDetailLine>(), invoice.Id, IsWholesale: true);
			}
			return new InvoiceDetail("Wholesale invoice", invoice.InvoiceNo, invoice.InvoiceAtUtc.ToBusinessLocal(), DisplayParty(customer?.Name), customer?.Phone ?? string.Empty, customer?.Address ?? string.Empty, string.Empty, string.Empty, string.Empty, invoice.PaymentStatus ?? string.Empty, invoice.Subtotal, invoice.TaxAmount, invoice.DiscountAmount, invoice.TotalAmount, invoice.PaidAmount, invoice.Notes ?? string.Empty, dictionary2.GetValueOrDefault(invoice.Id) ?? new List<InvoiceDetailLine>(), invoice.Id, IsWholesale: true);
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
			value.Add(Build(item.DrugId, item.BatchId, item.Quantity.ToString("0.##"), item.UnitPrice, item.TaxRate, item.DiscountAmount, item.LineTotal, item.ItemName, item.BatchNo, item.BatchExpiry));
		}
		foreach (WholesaleInvoiceItem item2 in invoiceItems.OrderBy((WholesaleInvoiceItem item) => item.CreatedAtUtc))
		{
			if (!dictionary.TryGetValue(item2.WholesaleInvoiceId, out var value2))
			{
				value2 = (dictionary[item2.WholesaleInvoiceId] = new List<InvoiceDetailLine>());
			}
			string quantity = ((item2.FreeQuantity > 0m) ? $"{item2.Quantity:0.##} + {item2.FreeQuantity:0.##} free" : item2.Quantity.ToString("0.##"));
			value2.Add(Build(item2.DrugId, item2.BatchId, quantity, item2.UnitPrice, item2.TaxRate, item2.DiscountAmount, item2.LineTotal, item2.ItemName, item2.BatchNo, item2.BatchExpiry));
		}
		return dictionary;
		InvoiceDetailLine Build(Guid drugId, Guid batchId, string quantity2, decimal unitPrice, decimal taxRate, decimal discount, decimal lineTotal, string? recordedName, string? recordedBatchNo, DateOnly? recordedExpiry)
		{
			Drug valueOrDefault = drugs.GetValueOrDefault(drugId);
			Batch valueOrDefault2 = batches.GetValueOrDefault(batchId);
			bool recorded = recordedName != null;
			return new InvoiceDetailLine(recorded ? recordedName! : valueOrDefault?.Name ?? "Unknown", string.IsNullOrWhiteSpace(valueOrDefault?.GenericName) ? string.Empty : valueOrDefault.GenericName, recorded ? recordedBatchNo ?? string.Empty : valueOrDefault2?.BatchNo ?? string.Empty, FormatExpiry(recorded ? recordedExpiry : valueOrDefault2?.ExpiryDate), quantity2, unitPrice, taxRate, discount, lineTotal);
		}
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

	public static readonly IReadOnlyList<string> PurchaseColumns = new[] { "Bill Date", "Entered At", "Bill No", "Supplier", "Payment Status", "Balance Due", "Subtotal", "GST", "Total", "Branch", "Supplier Bill" };

	// Each inwarded supplier bill: when it was billed and entered, whether it is paid, the amounts, its branch, and whether
	// the supplier's own bill is attached (a row opens it). Other branches publish only sales summaries, so the network
	// view has no other branch's purchases.
	private async Task<ReportTable> PurchasesAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, CancellationToken token)
	{
		Guid? currentBranchId = branchService.CurrentBranchId;
		IQueryable<PurchaseInvoice> source = from item in context.PurchaseInvoices.AsNoTracking()
			where (item.Status == PurchaseInvoice.PostedStatus || item.Status == PurchaseInvoice.CommittedStatus) && item.InvoiceDate >= @from && item.InvoiceDate <= to
			select item;
		if (branchScope == ReportBranchScope.CurrentBranchOnly && currentBranchId.HasValue)
		{
			source = source.Where((PurchaseInvoice item) => item.BranchId == null || item.BranchId == ((Guid?)currentBranchId).Value);
		}
		List<PurchaseInvoice> invoices = await source.ToListAsync(token);
		Dictionary<Guid, string> suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync((Supplier item) => item.Id, (Supplier item) => item.Name, token);
		Dictionary<Guid, string> branchCodes = await context.Branches.AsNoTracking().ToDictionaryAsync((Branch item) => item.Id, (Branch item) => item.Code, token);
		string currentCode = branchService.CurrentSettings.CurrentBranchCode;
		Guid[] supplierIds = invoices.Select(item => item.SupplierId).Distinct().ToArray();
		Dictionary<Guid, decimal> outstanding = await new SupplierBillBalanceService(context).GetBalancesAsync(supplierIds, token);
		List<PurchaseInvoice> ordered = invoices.OrderBy(item => item.InvoiceDate).ThenBy(item => item.CreatedAtUtc).ToList();
		IReadOnlyList<string>[] rows = ordered.Select(invoice =>
		{
			decimal? due = outstanding.TryGetValue(invoice.Id, out decimal value) ? value : null;
			return (IReadOnlyList<string>)new[]
			{
				invoice.InvoiceDate.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
				invoice.CreatedAtUtc.ToBusinessLocal().ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture),
				invoice.InvoiceNo,
				suppliers.GetValueOrDefault(invoice.SupplierId, string.Empty),
				SupplierBillBalanceService.StatusOf(due, invoice.TotalAmount),
				due.HasValue ? Format(due.Value) : string.Empty,
				Format(invoice.Subtotal),
				Format(invoice.TaxAmount),
				Format(invoice.TotalAmount),
				invoice.BranchId is Guid branchId && branchCodes.TryGetValue(branchId, out string? code) ? code : currentCode,
				string.IsNullOrWhiteSpace(PurchaseAttachmentPaths.For(invoice)) ? "—" : "Attached"
			};
		}).ToArray();
		return new ReportTable(
			(branchScope == ReportBranchScope.ConsolidatedNetwork) ? "Purchases / network" : "Purchases",
			PurchaseColumns,
			rows,
			ordered.Select(invoice => "P|" + invoice.Id).ToArray());
	}

	/// <summary>The supplier bill attached to a purchase (row key "P|id" in the Purchases report).</summary>
	public async Task<PurchaseBillAttachment?> GetPurchaseAttachmentAsync(string rowKey, CancellationToken token = default)
	{
		string[] parts = rowKey.Split('|');
		if (parts.Length != 2 || parts[0] != "P" || !Guid.TryParse(parts[1], out Guid id))
		{
			return null;
		}
		PurchaseInvoice? invoice = await context.PurchaseInvoices.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, token);
		if (invoice == null)
		{
			return null;
		}
		string supplier = await context.Suppliers.AsNoTracking().Where(item => item.Id == invoice.SupplierId).Select(item => item.Name).FirstOrDefaultAsync(token) ?? string.Empty;
		return new PurchaseBillAttachment(invoice.Id, invoice.InvoiceNo, supplier, PurchaseAttachmentPaths.For(invoice));
	}

	/// <summary>Every purchase return posted in the period, one row per return, with the supplier and reason.</summary>
	private async Task<ReportTable> PurchaseReturnBookAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		// Every value as recorded on the debit note when it was saved; nothing is read from the supplier or product master.
		List<PurchaseReturn> returns = await context.PurchaseReturns.AsNoTracking()
			.Where(item => item.ReturnDate >= from && item.ReturnDate <= to)
			.OrderBy(item => item.ReturnDate)
			.ThenBy(item => item.ReturnNo)
			.ToListAsync(token);
		Guid[] ids = returns.Select(item => item.Id).ToArray();
		Dictionary<Guid, (decimal Taxable, decimal Gst)> tax = (await context.PurchaseReturnItems.AsNoTracking()
				.Where(item => ids.Contains(item.PurchaseReturnId))
				.Select(item => new { item.PurchaseReturnId, Taxable = item.TaxableAmount ?? item.LineTotal, Gst = (item.CgstAmount ?? 0m) + (item.SgstAmount ?? 0m) + (item.IgstAmount ?? 0m) })
				.ToListAsync(token))
			.GroupBy(item => item.PurchaseReturnId)
			.ToDictionary(group => group.Key, group => (group.Sum(item => item.Taxable), group.Sum(item => item.Gst)));
		List<IReadOnlyList<string>> rows = returns
			.Select(item => (IReadOnlyList<string>)new[]
			{
				item.ReturnDate.ToString("yyyy-MM-dd"),
				item.ReturnNo,
				item.SellerLegalName ?? string.Empty,
				item.Reason,
				Format(tax.GetValueOrDefault(item.Id).Taxable),
				Format(tax.GetValueOrDefault(item.Id).Gst),
				Format(item.TotalAmount)
			})
			.ToList();
		return new ReportTable("Purchase return book", new[] { "Date", "Return no", "Supplier", "Reason", "Taxable", "GST", "Amount" }, rows);
	}

	/// <summary>Cash and bank movements in the period: money received from customers and counter sales, and paid to suppliers, by mode.</summary>
	private async Task<ReportTable> DayBookAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		(DateTime StartUtc, DateTime EndUtc) bounds = GetUtcBounds(from, to);
		List<Receipt> receipts = await context.Receipts.AsNoTracking()
			.Where(item => item.ReceiptAtUtc >= bounds.StartUtc && item.ReceiptAtUtc < bounds.EndUtc)
			.OrderBy(item => item.ReceiptAtUtc)
			.ToListAsync(token);
		Dictionary<Guid, string> customers = await context.Customers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
		Dictionary<Guid, string> suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
		decimal totalIn = 0m;
		decimal totalOut = 0m;
		List<IReadOnlyList<string>> rows = new List<IReadOnlyList<string>>();
		foreach (Receipt receipt in receipts)
		{
			// Refunds for voided retail bills are money paid out to the customer, even though they carry no supplier.
			bool refund = string.Equals(receipt.PaymentMethod, "Refund", StringComparison.OrdinalIgnoreCase);
			bool paidOut = receipt.SupplierId.HasValue || refund;
			string party = receipt.SupplierId.HasValue
				? suppliers.GetValueOrDefault(receipt.SupplierId.Value, "Supplier")
				: receipt.CustomerId.HasValue ? customers.GetValueOrDefault(receipt.CustomerId.Value, "Customer") : refund ? "Refund (voided bill)" : "Counter sale";
			if (paidOut)
			{
				totalOut += receipt.Amount;
			}
			else
			{
				totalIn += receipt.Amount;
			}

			rows.Add(new[]
			{
				receipt.ReceiptAtUtc.ToBusinessLocal().ToString("yyyy-MM-dd"),
				receipt.ReceiptNo,
				party,
				paidOut ? "Paid out" : "Received",
				receipt.PaymentMethod,
				Format(receipt.Amount),
				receipt.ReferenceNumber ?? receipt.ChequeNumber ?? string.Empty
			});
		}

		rows.Add(new[] { string.Empty, string.Empty, string.Empty, "Total received", string.Empty, Format(totalIn), string.Empty });
		rows.Add(new[] { string.Empty, string.Empty, string.Empty, "Total paid out", string.Empty, Format(totalOut), string.Empty });
		rows.Add(new[] { string.Empty, string.Empty, string.Empty, "Net cash movement", string.Empty, Format(totalIn - totalOut), string.Empty });
		return new ReportTable("Day book (cash & bank)", new[] { "Date", "Receipt no", "Party", "Direction", "Mode", "Amount", "Reference" }, rows);
	}

	/// <summary>Who owes whom as of a date, from the customer and supplier ledgers. Customers owing us are receivable; we owe suppliers payable.</summary>
	private async Task<ReportTable> PartyBalancesAsync(DateOnly asOf, CancellationToken token)
	{
		DateTime endUtc = asOf.AddDays(1).StartOfDayUtc();
		Dictionary<Guid, string> customers = await context.Customers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
		Dictionary<Guid, string> suppliers = await context.Suppliers.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Name, token);
		var customerNets = await context.CustomerLedgerEntries.AsNoTracking()
			.Where(item => item.EntryAtUtc < endUtc)
			.GroupBy(item => item.CustomerId)
			.Select(group => new { Id = group.Key, Net = group.Sum(item => item.Debit - item.Credit) })
			.ToListAsync(token);
		var supplierNets = await context.SupplierLedgerEntries.AsNoTracking()
			.Where(item => item.EntryAtUtc < endUtc)
			.GroupBy(item => item.SupplierId)
			.Select(group => new { Id = group.Key, Net = group.Sum(item => item.Credit - item.Debit) })
			.ToListAsync(token);

		decimal receivable = 0m;
		decimal payable = 0m;
		List<IReadOnlyList<string>> rows = new List<IReadOnlyList<string>>();
		foreach (var customer in customerNets.Where(item => item.Net != 0m).OrderBy(item => customers.GetValueOrDefault(item.Id, string.Empty)))
		{
			bool owesUs = customer.Net > 0m;
			if (owesUs)
			{
				receivable += customer.Net;
			}

			rows.Add(new[] { "Customer", customers.GetValueOrDefault(customer.Id, "Customer"), Format(Math.Abs(customer.Net)), owesUs ? "Receivable" : "Advance" });
		}

		foreach (var supplier in supplierNets.Where(item => item.Net != 0m).OrderBy(item => suppliers.GetValueOrDefault(item.Id, string.Empty)))
		{
			bool weOwe = supplier.Net > 0m;
			if (weOwe)
			{
				payable += supplier.Net;
			}

			rows.Add(new[] { "Supplier", suppliers.GetValueOrDefault(supplier.Id, "Supplier"), Format(Math.Abs(supplier.Net)), weOwe ? "Payable" : "Advance" });
		}

		rows.Add(new[] { string.Empty, "Total receivable", Format(receivable), string.Empty });
		rows.Add(new[] { string.Empty, "Total payable", Format(payable), string.Empty });
		return new ReportTable("Party balances as of " + asOf.ToString("dd-MMM-yyyy"), new[] { "Type", "Party", "Balance", "Side" }, rows);
	}

	/// <summary>A credit note's recorded document date; for one not yet backfilled, the day it was saved.</summary>
	private static DateOnly DocumentDate(ReturnNote note) => note.ReturnDate ?? note.ReturnAtUtc.ToBusinessDate();

	/// <summary>Credit notes issued against retail and wholesale sales in the period.</summary>
	private async Task<ReportTable> SaleReturnBookAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		// Every value as recorded on the credit note when it was saved; nothing is read from the party or product master.
		// The period is the note's own document date (a wholesale note may be dated before the day it was entered).
		(DateTime StartUtc, DateTime EndUtc) bounds = GetUtcBounds(from.AddDays(-31), to.AddDays(31));
		List<ReturnNote> notes = (await context.ReturnNotes.AsNoTracking()
				.Where(item => (item.SourceType == "Sale" || item.SourceType == "WholesaleCreditNote")
					&& item.ReturnAtUtc >= bounds.StartUtc && item.ReturnAtUtc < bounds.EndUtc)
				.ToListAsync(token))
			.Where(item => DocumentDate(item) >= from && DocumentDate(item) <= to)
			.OrderBy(item => DocumentDate(item))
			.ThenBy(item => item.ReturnNo)
			.ToList();
		Guid[] ids = notes.Select(item => item.Id).ToArray();
		var lines = (await context.SaleReturnItems.AsNoTracking()
				.Where(item => ids.Contains(item.ReturnNoteId))
				.Select(item => new { item.ReturnNoteId, Taxable = item.TaxableAmount ?? 0m, Gst = (item.CgstAmount ?? 0m) + (item.SgstAmount ?? 0m) + (item.IgstAmount ?? 0m) })
				.ToListAsync(token))
			.Concat(await context.WholesaleReturnItems.AsNoTracking()
				.Where(item => ids.Contains(item.ReturnNoteId))
				.Select(item => new { item.ReturnNoteId, Taxable = item.TaxableAmount ?? 0m, Gst = (item.CgstAmount ?? 0m) + (item.SgstAmount ?? 0m) + (item.IgstAmount ?? 0m) })
				.ToListAsync(token))
			.GroupBy(item => item.ReturnNoteId)
			.ToDictionary(group => group.Key, group => (Taxable: group.Sum(item => item.Taxable), Gst: group.Sum(item => item.Gst)));
		List<IReadOnlyList<string>> rows = notes
			.Select(item => (IReadOnlyList<string>)new[]
			{
				DocumentDate(item).ToString("yyyy-MM-dd"),
				item.ReturnNo,
				item.SourceType == "Sale" ? "Retail" : "Wholesale",
				item.BuyerLegalName ?? string.Empty,
				item.Reason,
				Format(lines.GetValueOrDefault(item.Id).Taxable),
				Format(lines.GetValueOrDefault(item.Id).Gst),
				Format(item.TotalAmount)
			})
			.ToList();
		rows.Add(new[] { string.Empty, string.Empty, string.Empty, string.Empty, "Total credit notes",
			Format(notes.Sum(item => lines.GetValueOrDefault(item.Id).Taxable)), Format(notes.Sum(item => lines.GetValueOrDefault(item.Id).Gst)), Format(notes.Sum(item => item.TotalAmount)) });
		return new ReportTable("Sale return book", new[] { "Date", "Credit note no", "Type", "Party", "Reason", "Taxable", "GST", "Amount" }, rows);
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
		Dictionary<Guid, string> branchCodes = await context.Branches.AsNoTracking().ToDictionaryAsync((Branch item) => item.Id, (Branch item) => item.Code, token);
		// Each document under its own branch; documents saved before branches were recorded belong to this one.
		string BranchOf(Guid? branchId) => branchId is Guid id && branchCodes.TryGetValue(id, out string? code) ? code : branchCode;
		// Filed documents read back as saved (name, batch, expiry, buyer); master data only for documents saved before recording began.
		string ItemOf(string? recorded, Drug? drug) => recorded ?? drug?.Name ?? "Unknown";
		string BatchOf(string? recordedName, string? recordedBatch, Batch? batch) => recordedName != null ? recordedBatch ?? string.Empty : batch?.BatchNo ?? string.Empty;
		string ExpiryOf(string? recordedName, DateOnly? recordedExpiry, Batch? batch) => FormatExpiry(recordedName != null ? recordedExpiry : batch?.ExpiryDate);
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
			BuyerSnapshot? recordedPatient = TaxDocumentSnapshot.Deserialize<BuyerSnapshot>(sale.BuyerSnapshotJson);
			string party = DisplayParty(recordedPatient?.Name ?? patient?.Name);
			// In billing order, as on the invoice.
			List<SaleItem> list2 = saleItems.Where((SaleItem item) => item.SaleId == sale.Id).OrderBy((SaleItem item) => item.CreatedAtUtc).ToList();
			if (list2.Count == 0)
			{
				list.Add(new SalesLineFact(sale.Id, $"R|{sale.Id}", sale.SaleAtUtc, sale.InvoiceNo, party, recordedPatient?.Phone ?? patient?.Phone ?? string.Empty, "—", "(no line items)", "(Unspecified)", "(Unspecified)", string.Empty, string.Empty, 0m, 0m, 0m, 0m, sale.TotalAmount, BranchOf(sale.BranchId)));
				continue;
			}
			foreach (SaleItem item in list2)
			{
				Drug valueOrDefault2 = drugs.GetValueOrDefault(item.DrugId);
				Batch valueOrDefault3 = batches.GetValueOrDefault(item.BatchId);
				decimal num = ((item.TaxRate <= 0m) ? item.LineTotal : decimal.Round(item.LineTotal / (1m + item.TaxRate / 100m), 2, MidpointRounding.AwayFromZero));
				list.Add(new SalesLineFact(sale.Id, $"R|{sale.Id}", sale.SaleAtUtc, sale.InvoiceNo, party, recordedPatient?.Phone ?? patient?.Phone ?? string.Empty, "—", ItemOf(item.ItemName, valueOrDefault2), BrandOf(valueOrDefault2), ManufacturerOf(valueOrDefault2), BatchOf(item.ItemName, item.BatchNo, valueOrDefault3), ExpiryOf(item.ItemName, item.BatchExpiry, valueOrDefault3), item.Quantity, item.UnitPrice, num, item.LineTotal - num, item.LineTotal, BranchOf(sale.BranchId)));
			}
		}
		foreach (WholesaleInvoice invoice in wholesale)
		{
			Customer valueOrDefault4 = customers.GetValueOrDefault(invoice.CustomerId);
			BuyerSnapshot? recordedBuyer = TaxDocumentSnapshot.Deserialize<BuyerSnapshot>(invoice.BuyerSnapshotJson);
			string party2 = DisplayParty(recordedBuyer != null ? invoice.BuyerLegalName ?? recordedBuyer.Name : valueOrDefault4?.Name);
			List<WholesaleInvoiceItem> list3 = invoiceItems.Where((WholesaleInvoiceItem item) => item.WholesaleInvoiceId == invoice.Id).OrderBy((WholesaleInvoiceItem item) => item.CreatedAtUtc).ToList();
			if (list3.Count == 0)
			{
				list.Add(new SalesLineFact(invoice.Id, $"W|{invoice.Id}", invoice.InvoiceAtUtc, invoice.InvoiceNo, party2, recordedBuyer?.Phone ?? valueOrDefault4?.Phone ?? string.Empty, recordedBuyer?.Gstin ?? valueOrDefault4?.Gstin ?? string.Empty, "(no line items)", "(Unspecified)", "(Unspecified)", string.Empty, string.Empty, 0m, 0m, 0m, 0m, invoice.TotalAmount, BranchOf(invoice.BranchId)));
				continue;
			}
			foreach (WholesaleInvoiceItem item2 in list3)
			{
				Drug valueOrDefault5 = drugs.GetValueOrDefault(item2.DrugId);
				Batch valueOrDefault6 = batches.GetValueOrDefault(item2.BatchId);
				decimal quantity = item2.Quantity + item2.FreeQuantity;
				list.Add(new SalesLineFact(invoice.Id, $"W|{invoice.Id}", invoice.InvoiceAtUtc, invoice.InvoiceNo, party2, recordedBuyer?.Phone ?? valueOrDefault4?.Phone ?? string.Empty, recordedBuyer?.Gstin ?? valueOrDefault4?.Gstin ?? string.Empty, ItemOf(item2.ItemName, valueOrDefault5), BrandOf(valueOrDefault5), ManufacturerOf(valueOrDefault5), BatchOf(item2.ItemName, item2.BatchNo, valueOrDefault6), ExpiryOf(item2.ItemName, item2.BatchExpiry, valueOrDefault6), quantity, item2.UnitPrice, item2.TaxableAmount, item2.CgstAmount + item2.SgstAmount + item2.IgstAmount, item2.LineTotal, BranchOf(invoice.BranchId)));
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
			where (item.Status == PurchaseInvoice.PostedStatus || item.Status == PurchaseInvoice.CommittedStatus) && item.InvoiceDate >= @from && item.InvoiceDate <= to
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
		return invoices.Where((WholesaleInvoice item) => item.InvoiceAtUtc.ToBusinessDate() <= asOf).Sum((WholesaleInvoice invoice) =>
		{
			decimal num = credits.Where((CustomerLedgerEntry entry) => entry.CustomerId == invoice.CustomerId && (entry.Notes?.Contains(invoice.InvoiceNo, StringComparison.OrdinalIgnoreCase) ?? false)).Sum((CustomerLedgerEntry entry) => entry.Credit);
			return Math.Max(0m, invoice.TotalAmount - Math.Max(invoice.PaidAmount, receipts.GetValueOrDefault(invoice.Id)) - num);
		});
	}

	private async Task<ReportTable> DailyMisAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		List<IReadOnlyList<string>> rows = new();
		for (DateOnly day = from; day <= to; day = day.AddDays(1))
		{
			DailyMisSummary mis = await salesAnalysis.GetDailyMisAsync(day, token);
			rows.Add(new[]
			{
				day.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
				Format(mis.GrossSales),
				Format(mis.PurchaseCost),
				Format(mis.GrossProfit),
				Format(mis.MarginPercent) + "%",
				mis.BillCount.ToString(CultureInfo.InvariantCulture),
				Format(mis.AvgBillValue)
			});
		}

		return new ReportTable(
			"Today's Gross Profit & Daily MIS (retail counter)",
			new[] { "Date", "Gross sales", "Purchase cost", "Gross profit ₹", "Margin %", "Bills", "Avg bill" },
			rows);
	}

	private async Task<ReportTable> AbcAnalysisAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		IReadOnlyList<AbcAnalysisRow> analysis = await salesAnalysis.GetAbcAnalysisAsync(from, to, token);
		IReadOnlyList<string>[] rows = analysis.Select(row => (IReadOnlyList<string>)new[]
		{
			row.Category,
			row.DrugName,
			row.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
			Format(row.SalesValue),
			Format(row.CumulativePercent) + "%"
		}).ToArray();
		return new ReportTable(
			"ABC Analysis (A=top 70% revenue, B=next 20%, C=bottom 10%)",
			new[] { "Category", "Drug", "Qty", "Sales value", "Cumulative %" },
			rows);
	}

	// Every invoice line in the selected period, oldest first; a row opens its invoice. Daily and monthly differ only in title:
	// the period is the report's From and To dates.
	private async Task<ReportTable> SaleBookAsync(DateOnly from, DateOnly to, ReportBranchScope branchScope, bool monthly, CancellationToken token)
	{
		List<SalesLineFact> lines = (await LoadSalesLineFactsAsync(from, to, branchScope, token))
			.OrderBy(item => item.AtUtc)
			.ThenBy(item => item.InvoiceNo, StringComparer.Ordinal)
			.ToList();
		IReadOnlyList<string>[] rows = lines.Select(item => (IReadOnlyList<string>)new[]
		{
			item.AtUtc.ToBusinessLocal().ToString("dd-MMM-yyyy HH:mm", CultureInfo.InvariantCulture),
			item.InvoiceNo,
			item.Party,
			item.Item,
			item.BatchNo,
			item.Expiry,
			item.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
			Format(item.UnitPrice),
			Format(item.LineTotal),
			item.BranchCode
		}).ToArray();
		return new ReportTable(
			monthly ? "Sale Book (monthly)" : "Sale Book (daily)",
			new[] { "Date & Time", "Invoice No", "Patient / Customer", "Item Name", "B.No", "Expiry (MM/YY)", "Qty", "Rate", "Total Amount", "Branch" },
			rows,
			lines.Select(item => item.RowKey).ToArray());
	}

	private async Task<ReportTable> PrescriberWiseAsync(DateOnly from, DateOnly to, CancellationToken token)
	{
		IReadOnlyList<PrescriberSaleRow> rowsData = await salesAnalysis.GetPrescriberWiseAsync(from, to, token);
		IReadOnlyList<string>[] rows = rowsData.Select(row => (IReadOnlyList<string>)new[]
		{
			row.PrescriberName,
			row.RegistrationNumber ?? string.Empty,
			row.Quantity.ToString("0.##", CultureInfo.InvariantCulture),
			Format(row.SalesValue),
			row.DispenseCount.ToString(CultureInfo.InvariantCulture)
		}).ToArray();
		return new ReportTable(
			"Doctor / Prescriber-wise dispensing",
			new[] { "Prescriber", "Registration", "Qty", "Sales value", "Dispenses" },
			rows);
	}

	private static (DateTime StartUtc, DateTime EndUtc) GetUtcBounds(DateOnly from, DateOnly to)
	{
		return (StartUtc: from.StartOfDayUtc(), EndUtc: to.AddDays(1).StartOfDayUtc());
	}

	private static string Format(decimal value)
	{
		return value.ToString("0.00", CultureInfo.InvariantCulture);
	}
}
