using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services.Dunning;

namespace PharmaBill.Data.Services;

/// <summary>
/// Real SQLite aggregates for the Wholesale B2B dashboard (no mock freezes).
/// </summary>
public sealed class WholesaleDashboardService(PharmaBillDbContext context, WholesaleAccountsService accounts, IDunningService dunning)
{
	public async Task<WholesaleDashboardSnapshot> GetSnapshotAsync(DateTime nowLocal, CancellationToken cancellationToken = default)
	{
		DateOnly today = DateOnly.FromDateTime(nowLocal);
		DateOnly yesterday = today.AddDays(-1);
		DateTime todayStartUtc = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime tomorrowStartUtc = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime yesterdayStartUtc = yesterday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime weekStartUtc = today.AddDays(-6).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

		List<WholesaleInvoice> allPosted = await context.WholesaleInvoices.AsNoTracking()
			.Where(i => i.Status == "Posted")
			.ToListAsync(cancellationToken);
		List<WholesaleInvoice> nonCancelled = await context.WholesaleInvoices.AsNoTracking()
			.Where(i => i.Status != "Cancelled")
			.ToListAsync(cancellationToken);

		List<WholesaleInvoice> todayInvoices = allPosted
			.Where(i => i.InvoiceAtUtc >= todayStartUtc && i.InvoiceAtUtc < tomorrowStartUtc)
			.ToList();
		decimal todayTurnover = Round(todayInvoices.Sum(i => i.TotalAmount));
		int todayCount = todayInvoices.Count;
		decimal yesterdayTurnover = Round(allPosted
			.Where(i => i.InvoiceAtUtc >= yesterdayStartUtc && i.InvoiceAtUtc < todayStartUtc)
			.Sum(i => i.TotalAmount));
		decimal trend = yesterdayTurnover <= 0m
			? (todayTurnover > 0m ? 100m : 0m)
			: Round((todayTurnover - yesterdayTurnover) * 100m / yesterdayTurnover);

		IReadOnlyList<CustomerAgeing> ageing = await accounts.GetAgeingAsync(today, cancellationToken);
		decimal ageing0 = Round(ageing.Sum(a => a.Current0To30));
		decimal ageing31 = Round(ageing.Sum(a => a.Days31To60));
		decimal ageing61 = Round(ageing.Sum(a => a.Days61To90));
		decimal ageing90 = Round(ageing.Sum(a => a.Over90));
		decimal outstanding = Round(ageing.Sum(a => a.Total));

		IReadOnlyList<DunningReceivableRow> receivables = await dunning.GetOpenReceivablesAsync(cancellationToken);
		decimal overdue45 = Round(receivables.Where(r => r.DaysPastDue > 45).Sum(r => r.OutstandingAmount));

		Dictionary<Guid, Customer> customers = await context.Customers.AsNoTracking()
			.ToDictionaryAsync(c => c.Id, cancellationToken);
		List<WholesaleOverdueBuyerRow> topOverdue = receivables
			.Where(r => r.DaysPastDue > 0)
			.GroupBy(r => r.CustomerId)
			.Select(g =>
			{
				Customer? customer = customers.GetValueOrDefault(g.Key);
				return new WholesaleOverdueBuyerRow(
					g.Key,
					g.First().CustomerName,
					g.First().Phone,
					customer?.CreditLimit ?? g.First().CreditLimit,
					Round(g.Sum(x => x.OutstandingAmount)),
					g.Max(x => x.DaysPastDue));
			})
			.OrderByDescending(r => r.OverdueAmount)
			.Take(5)
			.ToList();

		IReadOnlyList<(Guid SupplierId, string SupplierName, decimal Payable)> payables =
			await accounts.GetSupplierPayablesAsync(cancellationToken);
		decimal supplierPayables = Round(payables.Sum(p => p.Payable));

		DateOnly weekEnd = today.AddDays(7);
		List<PurchaseInvoice> openPurchases = await context.PurchaseInvoices.AsNoTracking()
			.Where(p =>
				(p.Status == PurchaseInvoice.PostedStatus || p.Status == PurchaseInvoice.CommittedStatus)
				&& p.DueDate != null
				&& p.DueDate >= today
				&& p.DueDate <= weekEnd)
			.ToListAsync(cancellationToken);
		decimal dueThisWeek = Round(openPurchases.Sum(p => Math.Max(0m, p.TotalAmount)));

		int draftOrders = nonCancelled.Count(i =>
			string.Equals(i.Status, "Draft", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(i.Status, "Open", StringComparison.OrdinalIgnoreCase));
		int readyToPack = todayInvoices.Count(i => !IsDispatched(i) && !IsFullyPaid(i));
		int dispatched = todayInvoices.Count(IsDispatched);
		int pendingEWay = allPosted.Count(i =>
			string.IsNullOrWhiteSpace(i.EWayBillNumber)
			&& i.TotalAmount >= 50000m
			&& i.InvoiceAtUtc >= today.AddDays(-7).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime());

		int pipelineReceived = todayCount;
		decimal pipelineReceivedValue = todayTurnover;
		int pipelinePicking = readyToPack;
		int pipelineDispatched = dispatched;
		int pipelineDelivered = todayInvoices.Count(IsFullyPaid);

		Guid[] weekInvoiceIds = allPosted
			.Where(i => i.InvoiceAtUtc >= weekStartUtc && i.InvoiceAtUtc < tomorrowStartUtc)
			.Select(i => i.Id)
			.ToArray();
		List<WholesaleInvoiceItem> weekItems = await context.WholesaleInvoiceItems.AsNoTracking()
			.Where(item => weekInvoiceIds.Contains(item.WholesaleInvoiceId))
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);
		List<Batch> batches = await context.Batches.AsNoTracking().Where(b => b.Quantity > 0m).ToListAsync(cancellationToken);
		Dictionary<Guid, decimal> stockByDrug = batches.GroupBy(b => b.DrugId)
			.ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
		Dictionary<Guid, string> topBatch = batches.GroupBy(b => b.DrugId)
			.ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.Quantity).First().BatchNo);

		List<WholesaleBulkVelocityRow> fastMoving = weekItems
			.GroupBy(i => i.DrugId)
			.Select(g =>
			{
				Drug? drug = drugs.GetValueOrDefault(g.Key);
				return new WholesaleBulkVelocityRow(
					g.Key,
					drug?.Name ?? "Unknown",
					g.Sum(i => i.Quantity + i.FreeQuantity),
					stockByDrug.GetValueOrDefault(g.Key),
					topBatch.GetValueOrDefault(g.Key));
			})
			.OrderByDescending(r => r.PacksSold)
			.Take(8)
			.ToList();

		int eInvoicesToday = todayInvoices.Count(i => !string.IsNullOrWhiteSpace(i.Irn));
		int pendingIrn = todayInvoices.Count(i => string.IsNullOrWhiteSpace(i.Irn));
		int eWayExpiring = allPosted.Count(i =>
			!string.IsNullOrWhiteSpace(i.EWayBillNumber)
			&& i.InvoiceAtUtc >= todayStartUtc.AddHours(-24)
			&& i.InvoiceAtUtc < tomorrowStartUtc
			&& !IsFullyPaid(i));

		return new WholesaleDashboardSnapshot(
			todayTurnover,
			todayCount,
			yesterdayTurnover,
			trend,
			outstanding,
			overdue45,
			supplierPayables,
			dueThisWeek,
			draftOrders,
			readyToPack,
			dispatched,
			pendingEWay,
			ageing0,
			ageing31,
			ageing61,
			ageing90,
			topOverdue,
			pipelineReceived,
			pipelineReceivedValue,
			pipelinePicking,
			pipelineDispatched,
			pipelineDelivered,
			fastMoving,
			eInvoicesToday,
			pendingIrn,
			eWayExpiring);
	}

	private static bool IsDispatched(WholesaleInvoice invoice)
		=> !string.IsNullOrWhiteSpace(invoice.VehicleNumber)
			|| !string.IsNullOrWhiteSpace(invoice.EWayBillNumber)
			|| !string.IsNullOrWhiteSpace(invoice.TransportDetails);

	private static bool IsFullyPaid(WholesaleInvoice invoice)
		=> invoice.PaidAmount >= invoice.TotalAmount
			|| string.Equals(invoice.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase);

	private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
