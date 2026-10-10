using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record DumpStockRow(
	Guid BatchId,
	Guid DrugId,
	string DrugName,
	string BatchNo,
	DateOnly? ExpiryDate,
	decimal Quantity,
	decimal PurchaseRate,
	decimal BlockedCapital,
	int IdleDays,
	DateOnly? LastSaleDate);

public sealed record StockValuationSummary(
	decimal FifoPurchaseValue,
	decimal MrpValue,
	int BatchCount,
	int DrugCount);

public sealed record StockValuationRow(
	Guid DrugId,
	string DrugName,
	string BatchNo,
	decimal Quantity,
	decimal PurchaseRate,
	decimal Mrp,
	decimal FifoValue,
	decimal MrpHoldingValue);

public sealed record ReorderAlertRow(
	Guid DrugId,
	string DrugName,
	decimal StockQty,
	decimal ReorderLevel,
	decimal MaxStockLevel,
	decimal SuggestedOrderQty,
	string? PrimarySupplierName,
	Guid? PrimarySupplierId);

public sealed record BannedDrugRow(
	Guid DrugId,
	Guid? BatchId,
	string DrugName,
	string? BatchNo,
	string Status,
	string? Reason);

public sealed record ExpiryLifeRow(
	Guid BatchId,
	Guid DrugId,
	string DrugName,
	string BatchNo,
	DateOnly? ExpiryDate,
	decimal Quantity,
	decimal PurchaseRate,
	decimal BlockedCapital,
	string Band);

public sealed class StockIntelligenceService(PharmaBillDbContext context)
{
	public async Task<IReadOnlyList<DumpStockRow>> GetDumpStockAsync(int idleDays, CancellationToken cancellationToken = default)
	{
		if (idleDays is not (60 or 90 or 180))
		{
			idleDays = 90;
		}

		DateTime cutoffUtc = DateOnly.FromDateTime(DateTime.Today).AddDays(-idleDays)
			.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

		List<Batch> batches = await context.Batches.AsNoTracking()
			.Where(b => b.Quantity > 0m)
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);

		var retailMoves = await (from item in context.SaleItems.AsNoTracking()
			join sale in context.Sales.AsNoTracking() on item.SaleId equals sale.Id
			where sale.SaleAtUtc >= cutoffUtc
			group item by item.BatchId into g
			select new { BatchId = g.Key, LastAt = g.Max(x => x.CreatedAtUtc) }).ToListAsync(cancellationToken);

		var wholesaleMoves = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
			join inv in context.WholesaleInvoices.AsNoTracking() on item.WholesaleInvoiceId equals inv.Id
			where inv.Status == "Posted" && inv.InvoiceAtUtc >= cutoffUtc
			group item by item.BatchId into g
			select new { BatchId = g.Key, LastAt = g.Max(x => x.CreatedAtUtc) }).ToListAsync(cancellationToken);

		HashSet<Guid> activeBatchIds = retailMoves.Select(x => x.BatchId)
			.Concat(wholesaleMoves.Select(x => x.BatchId))
			.ToHashSet();

		Dictionary<Guid, DateTime> lastSale = retailMoves
			.Concat(wholesaleMoves)
			.GroupBy(x => x.BatchId)
			.ToDictionary(g => g.Key, g => g.Max(x => x.LastAt));

		List<DumpStockRow> rows = new();
		foreach (Batch batch in batches.Where(b => !activeBatchIds.Contains(b.Id)))
		{
			if (!drugs.TryGetValue(batch.DrugId, out Drug? drug) || !drug.IsActive)
			{
				continue;
			}

			DateOnly? lastSaleDate = lastSale.TryGetValue(batch.Id, out DateTime at)
				? DateOnly.FromDateTime(DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime())
				: null;
			int idle = lastSaleDate.HasValue
				? Math.Max(idleDays, DateOnly.FromDateTime(DateTime.Today).DayNumber - lastSaleDate.Value.DayNumber)
				: idleDays;
			rows.Add(new DumpStockRow(
				batch.Id,
				batch.DrugId,
				drug.Name,
				batch.BatchNo,
				batch.ExpiryDate,
				batch.Quantity,
				batch.PurchasePrice,
				Round(batch.Quantity * batch.PurchasePrice),
				idle,
				lastSaleDate));
		}

		return rows.OrderByDescending(r => r.BlockedCapital).ThenBy(r => r.DrugName).ToList();
	}

	public async Task<StockValuationSummary> GetValuationSummaryAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<StockValuationRow> rows = await GetValuationRowsAsync(cancellationToken);
		return new StockValuationSummary(
			Round(rows.Sum(r => r.FifoValue)),
			Round(rows.Sum(r => r.MrpHoldingValue)),
			rows.Count,
			rows.Select(r => r.DrugId).Distinct().Count());
	}

	public async Task<IReadOnlyList<StockValuationRow>> GetValuationRowsAsync(CancellationToken cancellationToken = default)
	{
		List<Batch> batches = await context.Batches.AsNoTracking()
			.Where(b => b.Quantity > 0m)
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);
		return batches
			.Where(b => drugs.ContainsKey(b.DrugId))
			.Select(b =>
			{
				Drug drug = drugs[b.DrugId];
				decimal mrp = b.Mrp ?? drug.Mrp ?? 0m;
				return new StockValuationRow(
					b.DrugId,
					drug.Name,
					b.BatchNo,
					b.Quantity,
					b.PurchasePrice,
					mrp,
					Round(b.Quantity * b.PurchasePrice),
					Round(b.Quantity * mrp));
			})
			.OrderBy(r => r.DrugName)
			.ThenBy(r => r.BatchNo)
			.ToList();
	}

	public async Task<IReadOnlyList<ReorderAlertRow>> GetReorderAlertsAsync(CancellationToken cancellationToken = default)
	{
		List<Drug> drugs = await context.Drugs.AsNoTracking()
			.Where(d => d.IsActive && !d.IsBanned)
			.ToListAsync(cancellationToken);
		var qty = await context.Batches.AsNoTracking()
			.GroupBy(b => b.DrugId)
			.Select(g => new { DrugId = g.Key, Qty = g.Sum(b => b.Quantity) })
			.ToListAsync(cancellationToken);
		Dictionary<Guid, decimal> qtyMap = qty.ToDictionary(x => x.DrugId, x => x.Qty);

		var primarySupplier = await (from batch in context.Batches.AsNoTracking()
			where batch.SupplierId != null && batch.Quantity > 0m
			group batch by batch.DrugId into g
			select new
			{
				DrugId = g.Key,
				SupplierId = g.OrderByDescending(b => b.Quantity).Select(b => b.SupplierId).FirstOrDefault()
			}).ToListAsync(cancellationToken);
		Dictionary<Guid, Guid?> supplierByDrug = primarySupplier.ToDictionary(x => x.DrugId, x => x.SupplierId);
		Dictionary<Guid, string> suppliers = await context.Suppliers.AsNoTracking()
			.ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

		List<ReorderAlertRow> rows = new();
		foreach (Drug drug in drugs)
		{
			decimal stock = qtyMap.GetValueOrDefault(drug.Id);
			if (stock > drug.ReorderLevel)
			{
				continue;
			}

			decimal max = drug.MaxStockLevel > 0m ? drug.MaxStockLevel : Math.Max(drug.ReorderLevel * 3m, drug.ReorderLevel + 1m);
			decimal suggested = Math.Max(0m, max - stock);
			Guid? supplierId = supplierByDrug.GetValueOrDefault(drug.Id);
			rows.Add(new ReorderAlertRow(
				drug.Id,
				drug.Name,
				stock,
				drug.ReorderLevel,
				max,
				suggested,
				supplierId.HasValue ? suppliers.GetValueOrDefault(supplierId.Value) : null,
				supplierId));
		}

		return rows.OrderBy(r => r.PrimarySupplierName ?? "~").ThenBy(r => r.DrugName).ToList();
	}

	public async Task SetBanAsync(Guid drugId, Guid? batchId, bool banned, string? reason, CancellationToken cancellationToken = default)
	{
		Drug drug = await context.Drugs.SingleAsync(d => d.Id == drugId, cancellationToken);
		if (batchId is null)
		{
			drug.IsBanned = banned;
		}
		else
		{
			Batch batch = await context.Batches.SingleAsync(b => b.Id == batchId && b.DrugId == drugId, cancellationToken);
			batch.IsBanned = banned;
			batch.BanReason = banned ? (reason ?? "Banned / Not for Sale / Recalled") : null;
		}

		await context.SaveChangesAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<BannedDrugRow>> GetBannedRegistryAsync(CancellationToken cancellationToken = default)
	{
		List<BannedDrugRow> rows = new();
		foreach (Drug drug in await context.Drugs.AsNoTracking().Where(d => d.IsBanned).ToListAsync(cancellationToken))
		{
			rows.Add(new BannedDrugRow(drug.Id, null, drug.Name, null, "Banned", "Drug-level ban"));
		}

		List<Batch> bannedBatches = await context.Batches.AsNoTracking().Where(b => b.IsBanned).ToListAsync(cancellationToken);
		Dictionary<Guid, string> names = await context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
		foreach (Batch batch in bannedBatches)
		{
			rows.Add(new BannedDrugRow(
				batch.DrugId,
				batch.Id,
				names.GetValueOrDefault(batch.DrugId, "Unknown"),
				batch.BatchNo,
				"Banned / Recalled",
				batch.BanReason));
		}

		return rows.OrderBy(r => r.DrugName).ToList();
	}

	public async Task<IReadOnlyList<ExpiryLifeRow>> GetExpiryLifeAsync(string band, CancellationToken cancellationToken = default)
	{
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<Batch> batches = await context.Batches.AsNoTracking().Where(b => b.Quantity > 0m && b.ExpiryDate != null).ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);
		string normalized = (band ?? "All").Trim();

		IEnumerable<Batch> filtered = normalized switch
		{
			"Expired" => batches.Where(b => b.ExpiryDate < today),
			"< 30 Days" or "30" => batches.Where(b => b.ExpiryDate >= today && b.ExpiryDate <= today.AddDays(30)),
			"< 60 Days" or "60" => batches.Where(b => b.ExpiryDate >= today && b.ExpiryDate <= today.AddDays(60)),
			"< 90 Days" or "90" or "Near expiry" => batches.Where(b => b.ExpiryDate >= today && b.ExpiryDate <= today.AddDays(90)),
			_ => batches.Where(b => b.ExpiryDate < today || b.ExpiryDate <= today.AddDays(90))
		};

		return filtered
			.Where(b => drugs.ContainsKey(b.DrugId))
			.Select(b =>
			{
				string rowBand = b.ExpiryDate < today
					? "Expired"
					: b.ExpiryDate <= today.AddDays(30)
						? "< 30 Days"
						: b.ExpiryDate <= today.AddDays(60)
							? "< 60 Days"
							: "< 90 Days";
				return new ExpiryLifeRow(
					b.Id,
					b.DrugId,
					drugs[b.DrugId].Name,
					b.BatchNo,
					b.ExpiryDate,
					b.Quantity,
					b.PurchasePrice,
					Round(b.Quantity * b.PurchasePrice),
					rowBand);
			})
			.OrderBy(r => r.ExpiryDate)
			.ThenBy(r => r.DrugName)
			.ToList();
	}

	private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
