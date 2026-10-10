using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record DailyMisSummary(
	DateOnly Day,
	decimal GrossSales,
	decimal PurchaseCost,
	decimal GrossProfit,
	decimal MarginPercent,
	int BillCount,
	decimal AvgBillValue);

public sealed record AbcAnalysisRow(
	Guid DrugId,
	string DrugName,
	decimal SalesValue,
	decimal Quantity,
	decimal CumulativePercent,
	string Category);

public sealed record SaleBookRow(
	string PeriodKey,
	DateOnly PeriodStart,
	decimal InvoiceCount,
	decimal Taxable,
	decimal Tax,
	decimal GrossSales);

public sealed record BrandSaleRow(
	string BrandOrCompany,
	decimal Quantity,
	decimal SalesValue,
	decimal BillCount);

public sealed record PrescriberSaleRow(
	string PrescriberName,
	string? RegistrationNumber,
	decimal Quantity,
	decimal SalesValue,
	int DispenseCount);

public sealed class SalesAnalysisService(PharmaBillDbContext context)
{
	public async Task<DailyMisSummary> GetDailyMisAsync(DateOnly day, CancellationToken cancellationToken = default)
	{
		(DateTime startUtc, DateTime endUtc) = GetUtcBounds(day, day);
		List<Sale> sales = await context.Sales.AsNoTracking()
			.Where(s => s.SaleAtUtc >= startUtc && s.SaleAtUtc < endUtc)
			.ToListAsync(cancellationToken);
		Guid[] saleIds = sales.Select(s => s.Id).ToArray();
		List<SaleItem> items = await context.SaleItems.AsNoTracking()
			.Where(i => saleIds.Contains(i.SaleId))
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Batch> batches = await context.Batches.AsNoTracking().ToDictionaryAsync(b => b.Id, cancellationToken);

		decimal gross = Round(items.Sum(i => i.LineTotal));
		decimal cost = Round(items.Sum(i =>
		{
			decimal rate = batches.TryGetValue(i.BatchId, out Batch? batch) ? batch.PurchasePrice : 0m;
			return i.Quantity * rate;
		}));
		decimal profit = Round(gross - cost);
		decimal margin = gross > 0m ? Round(profit * 100m / gross) : 0m;
		return new DailyMisSummary(
			day,
			gross,
			cost,
			profit,
			margin,
			sales.Count,
			sales.Count == 0 ? 0m : Round(sales.Sum(s => s.TotalAmount) / sales.Count));
	}

	public async Task<IReadOnlyList<AbcAnalysisRow>> GetAbcAnalysisAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
	{
		(DateTime startUtc, DateTime endUtc) = GetUtcBounds(from, to);
		List<Sale> sales = await context.Sales.AsNoTracking()
			.Where(s => s.SaleAtUtc >= startUtc && s.SaleAtUtc < endUtc)
			.ToListAsync(cancellationToken);
		Guid[] saleIds = sales.Select(s => s.Id).ToArray();
		List<SaleItem> items = await context.SaleItems.AsNoTracking()
			.Where(i => saleIds.Contains(i.SaleId))
			.ToListAsync(cancellationToken);
		Dictionary<Guid, string> drugs = await context.Drugs.AsNoTracking()
			.ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);

		var ranked = items
			.GroupBy(i => i.DrugId)
			.Select(g => new
			{
				DrugId = g.Key,
				Sales = Round(g.Sum(i => i.LineTotal)),
				Qty = g.Sum(i => i.Quantity)
			})
			.OrderByDescending(x => x.Sales)
			.ToList();
		decimal total = ranked.Sum(x => x.Sales);
		decimal cumulative = 0m;
		List<AbcAnalysisRow> rows = new();
		foreach (var row in ranked)
		{
			cumulative += row.Sales;
			decimal cumPct = total > 0m ? Round(cumulative * 100m / total) : 0m;
			string category = cumPct <= 70m ? "A" : cumPct <= 90m ? "B" : "C";
			// Reclassify edge: first items that push past 70 stay A if previous was under 70
			if (rows.Count == 0 && category != "A")
			{
				category = "A";
			}
			else if (rows.Count > 0)
			{
				decimal prevCum = rows[^1].CumulativePercent;
				if (prevCum < 70m && cumPct > 70m && cumPct <= 90m)
				{
					category = "A";
				}
				else if (prevCum < 90m && cumPct > 90m)
				{
					category = row.Sales / Math.Max(total, 0.01m) >= 0.02m && prevCum < 90m ? "B" : "C";
					if (prevCum < 90m)
					{
						category = "B";
					}
				}
			}

			rows.Add(new AbcAnalysisRow(
				row.DrugId,
				drugs.GetValueOrDefault(row.DrugId, "Unknown"),
				row.Sales,
				row.Qty,
				cumPct,
				category));
		}

		// Final pass: enforce A=top 70%, B=next 20%, C=bottom 10% by cumulative revenue
		List<AbcAnalysisRow> normalized = new();
		foreach (AbcAnalysisRow row in rows)
		{
			string cat = row.CumulativePercent <= 70m ? "A" : row.CumulativePercent <= 90m ? "B" : "C";
			normalized.Add(row with { Category = cat });
		}

		return normalized;
	}

	public async Task<IReadOnlyList<SaleBookRow>> GetSaleBookAsync(DateOnly from, DateOnly to, bool groupByMonth, CancellationToken cancellationToken = default)
	{
		(DateTime startUtc, DateTime endUtc) = GetUtcBounds(from, to);
		List<Sale> retail = await context.Sales.AsNoTracking()
			.Where(s => s.SaleAtUtc >= startUtc && s.SaleAtUtc < endUtc)
			.ToListAsync(cancellationToken);
		List<WholesaleInvoice> wholesale = await context.WholesaleInvoices.AsNoTracking()
			.Where(i => i.Status == "Posted" && i.InvoiceAtUtc >= startUtc && i.InvoiceAtUtc < endUtc)
			.ToListAsync(cancellationToken);

		var facts = retail.Select(s => new
		{
			Local = DateTime.SpecifyKind(s.SaleAtUtc, DateTimeKind.Utc).ToLocalTime(),
			Taxable = s.Subtotal,
			Tax = s.TaxAmount,
			Total = s.TotalAmount
		}).Concat(wholesale.Select(i => new
		{
			Local = DateTime.SpecifyKind(i.InvoiceAtUtc, DateTimeKind.Utc).ToLocalTime(),
			Taxable = i.Subtotal,
			Tax = i.TaxAmount,
			Total = i.TotalAmount
		})).ToList();

		return facts
			.GroupBy(f => groupByMonth
				? new DateOnly(f.Local.Year, f.Local.Month, 1)
				: DateOnly.FromDateTime(f.Local))
			.OrderBy(g => g.Key)
			.Select(g => new SaleBookRow(
				groupByMonth ? g.Key.ToString("MMM yyyy", CultureInfo.InvariantCulture) : g.Key.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
				g.Key,
				g.Count(),
				Round(g.Sum(x => x.Taxable)),
				Round(g.Sum(x => x.Tax)),
				Round(g.Sum(x => x.Total))))
			.ToList();
	}

	public async Task<IReadOnlyList<BrandSaleRow>> GetBrandWiseSalesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
	{
		(DateTime startUtc, DateTime endUtc) = GetUtcBounds(from, to);
		List<Sale> sales = await context.Sales.AsNoTracking()
			.Where(s => s.SaleAtUtc >= startUtc && s.SaleAtUtc < endUtc)
			.ToListAsync(cancellationToken);
		Guid[] saleIds = sales.Select(s => s.Id).ToArray();
		List<SaleItem> items = await context.SaleItems.AsNoTracking()
			.Where(i => saleIds.Contains(i.SaleId))
			.ToListAsync(cancellationToken);
		Dictionary<Guid, Drug> drugs = await context.Drugs.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);

		return items
			.GroupBy(i =>
			{
				Drug? drug = drugs.GetValueOrDefault(i.DrugId);
				return string.IsNullOrWhiteSpace(drug?.BrandName) ? (drug?.Name ?? "Unknown") : drug!.BrandName!;
			})
			.Select(g => new BrandSaleRow(
				g.Key,
				g.Sum(i => i.Quantity),
				Round(g.Sum(i => i.LineTotal)),
				g.Select(i => i.SaleId).Distinct().Count()))
			.OrderByDescending(r => r.SalesValue)
			.ToList();
	}

	public async Task<IReadOnlyList<PrescriberSaleRow>> GetPrescriberWiseAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
	{
		(DateTime startUtc, DateTime endUtc) = GetUtcBounds(from, to);
		List<Sale> sales = await context.Sales.AsNoTracking()
			.Where(s => s.SaleAtUtc >= startUtc && s.SaleAtUtc < endUtc && s.PrescriptionId != null)
			.ToListAsync(cancellationToken);
		Guid[] prescriptionIds = sales.Where(s => s.PrescriptionId.HasValue).Select(s => s.PrescriptionId!.Value).Distinct().ToArray();
		Dictionary<Guid, Prescription> prescriptions = await context.Prescriptions.AsNoTracking()
			.Where(p => prescriptionIds.Contains(p.Id))
			.ToDictionaryAsync(p => p.Id, cancellationToken);
		Guid[] saleIds = sales.Select(s => s.Id).ToArray();
		List<SaleItem> items = await context.SaleItems.AsNoTracking()
			.Where(i => saleIds.Contains(i.SaleId))
			.ToListAsync(cancellationToken);
		Dictionary<Guid, List<SaleItem>> itemsBySale = items.GroupBy(i => i.SaleId).ToDictionary(g => g.Key, g => g.ToList());

		return sales
			.GroupBy(s =>
			{
				if (s.PrescriptionId.HasValue && prescriptions.TryGetValue(s.PrescriptionId.Value, out Prescription? rx))
				{
					return (Name: string.IsNullOrWhiteSpace(rx.PrescriberName) ? "Unnamed prescriber" : rx.PrescriberName!, Reg: rx.PrescriberRegistrationNumber);
				}

				return (Name: "No prescriber", Reg: (string?)null);
			})
			.Select(g =>
			{
				decimal qty = 0m;
				decimal value = 0m;
				foreach (Sale sale in g)
				{
					if (!itemsBySale.TryGetValue(sale.Id, out List<SaleItem>? lineItems))
					{
						continue;
					}

					qty += lineItems.Sum(i => i.Quantity);
					value += lineItems.Sum(i => i.LineTotal);
				}

				return new PrescriberSaleRow(g.Key.Name, g.Key.Reg, qty, Round(value), g.Count());
			})
			.OrderByDescending(r => r.SalesValue)
			.ToList();
	}

	private static (DateTime StartUtc, DateTime EndUtc) GetUtcBounds(DateOnly from, DateOnly to)
	{
		DateTime startUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime endUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		return (startUtc, endUtc);
	}

	private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
