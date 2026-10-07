using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class WholesalePricingService(IUnitOfWork unitOfWork)
{
	public async Task SaveBatchRatesAsync(Guid batchId, WholesaleBatchRate rates, Guid userId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!(rates.PurchaseRate < 0m))
		{
			decimal? ptr = rates.Ptr;
			if (!((ptr.GetValueOrDefault() < 0m) & ptr.HasValue))
			{
				ptr = rates.Pts;
				if (!((ptr.GetValueOrDefault() < 0m) & ptr.HasValue) && !(rates.Mrp <= 0m))
				{
					if (rates.Ptr.HasValue)
					{
						ptr = rates.Ptr;
						decimal mrp = rates.Mrp;
						if ((ptr.GetValueOrDefault() > mrp) & ptr.HasValue)
						{
							goto IL_0162;
						}
					}
					if (rates.Pts.HasValue)
					{
						ptr = rates.Pts;
						decimal mrp = rates.Mrp;
						if ((ptr.GetValueOrDefault() > mrp) & ptr.HasValue)
						{
							goto IL_0162;
						}
					}
					await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
					Batch batch = await unitOfWork.Context.Batches.SingleAsync((Batch item) => item.Id == batchId, cancellationToken);
					batch.PurchasePrice = rates.PurchaseRate;
					batch.Ptr = rates.Ptr;
					batch.Pts = rates.Pts;
					batch.Mrp = rates.Mrp;
					unitOfWork.Context.AuditLogs.Add(new AuditLog
					{
						UserId = userId,
						ActionAtUtc = DateTime.UtcNow,
						Action = "WholesaleBatchRatesUpdated",
						EntityName = "Batch",
						EntityId = batch.Id,
						Details = $"PTR {rates.Ptr}; PTS {rates.Pts}; MRP {rates.Mrp}."
					});
					await unitOfWork.SaveChangesAsync(cancellationToken);
					await transaction.CommitAsync(cancellationToken);
					return;
				}
			}
		}
		goto IL_0162;
		IL_0162:
		throw new InvalidOperationException("Rates must be non-negative and PTR/PTS cannot exceed batch MRP.");
	}

	public async Task SavePackLevelsAsync(Guid drugId, IReadOnlyCollection<DrugPackLevel> packLevels, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (packLevels.Count == 0 || packLevels.Any((DrugPackLevel level) => level.UnitsPerPack <= 0m || string.IsNullOrWhiteSpace(level.Label)) || packLevels.Select((DrugPackLevel level) => level.Level).Distinct().Count() != packLevels.Count)
		{
			throw new InvalidOperationException("Pack levels need unique levels, labels, and positive base-unit conversions.");
		}
		DrugPackLevel? drugPackLevel = packLevels.SingleOrDefault((DrugPackLevel level) => level.Level == PackLevel.Unit);
		if (drugPackLevel == null || drugPackLevel.UnitsPerPack != 1m)
		{
			throw new InvalidOperationException("The unit pack level must convert to exactly one base unit.");
		}
		if (!(await unitOfWork.Context.Drugs.AnyAsync((Drug drug) => drug.Id == drugId, cancellationToken)))
		{
			throw new InvalidOperationException("The selected medicine does not exist.");
		}
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		List<DrugPackLevel> entities = await unitOfWork.Context.DrugPackLevels.Where((DrugPackLevel level) => level.DrugId == drugId).ToListAsync(cancellationToken);
		unitOfWork.Context.DrugPackLevels.RemoveRange(entities);
		foreach (DrugPackLevel packLevel in packLevels)
		{
			unitOfWork.Context.DrugPackLevels.Add(new DrugPackLevel
			{
				DrugId = drugId,
				Level = packLevel.Level,
				Label = packLevel.Label.Trim(),
				UnitsPerPack = packLevel.UnitsPerPack
			});
		}
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task SaveCategoryPriceAsync(CustomerCategoryPrice price, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(price.PriceCategory) || price.UnitPrice < 0m || (price.EffectiveFrom.HasValue && price.EffectiveTo.HasValue && price.EffectiveTo < price.EffectiveFrom))
		{
			throw new InvalidOperationException("Enter a valid category price and effective period.");
		}
		if (price.DrugId.HasValue)
		{
			Drug drug = (await unitOfWork.Context.Drugs.AsNoTracking().SingleOrDefaultAsync((Drug item) => item.Id == price.DrugId, cancellationToken)) ?? throw new InvalidOperationException("The selected medicine does not exist.");
			decimal? num = (await (from batch in unitOfWork.Context.Batches.AsNoTracking()
				where batch.DrugId == drug.Id && batch.Mrp.HasValue
				select batch.Mrp).MinAsync(cancellationToken)) ?? drug.Mrp;
			if (num.HasValue && price.UnitPrice > num.Value)
			{
				throw new InvalidOperationException("Customer category price cannot exceed the medicine's MRP.");
			}
		}
		unitOfWork.Context.CustomerCategoryPrices.Add(price);
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	public async Task SaveTradeDiscountAsync(TradeDiscount discount, CancellationToken cancellationToken = default(CancellationToken))
	{
		bool flag = string.IsNullOrWhiteSpace(discount.PriceCategory);
		if (!flag)
		{
			decimal discountPercent = discount.DiscountPercent;
			bool flag2 = ((discountPercent < 0m || discountPercent > 100m) ? true : false);
			flag = flag2;
		}
		if (flag || (discount.EffectiveFrom.HasValue && discount.EffectiveTo.HasValue && discount.EffectiveTo < discount.EffectiveFrom))
		{
			throw new InvalidOperationException("Trade discount must be between 0 and 100 percent with a valid date range.");
		}
		unitOfWork.Context.TradeDiscounts.Add(discount);
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	public async Task SaveSchemeAsync(WholesaleScheme scheme, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (scheme.BuyQuantity <= 0m || scheme.FreeQuantity <= 0m || scheme.EndsOn < scheme.StartsOn)
		{
			throw new InvalidOperationException("A scheme needs positive buy/free quantities and a valid date range.");
		}
		unitOfWork.Context.WholesaleSchemes.Add(scheme);
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	public async Task<WholesalePriceQuote> GetQuoteAsync(Customer customer, Drug drug, Batch batch, decimal paidQuantity, DateOnly date, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (paidQuantity <= 0m)
		{
			throw new ArgumentOutOfRangeException("paidQuantity", "Paid quantity must be positive.");
		}
		decimal mrp = batch.Mrp ?? drug.Mrp ?? throw new InvalidOperationException("Batch MRP is required to quote a wholesale price.");
		decimal baseRate = (string.IsNullOrWhiteSpace(customer.PriceCategory) ? ((decimal?)null) : (await ((IQueryable<CustomerCategoryPrice>)(from price in unitOfWork.Context.CustomerCategoryPrices.AsNoTracking()
			where (!price.DrugId.HasValue || price.DrugId == drug.Id) && price.PriceCategory == customer.PriceCategory && (!price.EffectiveFrom.HasValue || price.EffectiveFrom.Value <= date) && (!price.EffectiveTo.HasValue || price.EffectiveTo.Value >= date)
			orderby price.EffectiveFrom descending, price.DrugId == drug.Id descending
			select price)).Select((Expression<Func<CustomerCategoryPrice, decimal?>>)((CustomerCategoryPrice price) => price.UnitPrice)).FirstOrDefaultAsync(cancellationToken))) ?? batch.Pts ?? batch.Ptr ?? batch.PurchasePrice;
		decimal num = (string.IsNullOrWhiteSpace(customer.PriceCategory) ? 0m : (await ((IQueryable<TradeDiscount>)(from discount in unitOfWork.Context.TradeDiscounts.AsNoTracking()
			where discount.PriceCategory == customer.PriceCategory && (!discount.DrugId.HasValue || discount.DrugId == drug.Id) && (!discount.EffectiveFrom.HasValue || discount.EffectiveFrom.Value <= date) && (!discount.EffectiveTo.HasValue || discount.EffectiveTo.Value >= date)
			orderby discount.DrugId == drug.Id descending, discount.EffectiveFrom descending
			select discount)).Select((Expression<Func<TradeDiscount, decimal?>>)((TradeDiscount discount) => discount.DiscountPercent)).FirstOrDefaultAsync(cancellationToken)).GetValueOrDefault());
		decimal discountPercent = num;
		decimal unitRate = decimal.Round(baseRate * (1m - discountPercent / 100m), 2, MidpointRounding.AwayFromZero);
		if (unitRate > mrp)
		{
			throw new InvalidOperationException("Wholesale price for " + drug.Name + " cannot exceed batch MRP.");
		}
		var anon = await (from scheme in unitOfWork.Context.WholesaleSchemes.AsNoTracking()
			where (!scheme.DrugId.HasValue || scheme.DrugId == drug.Id) && (scheme.PriceCategory == null || scheme.PriceCategory == customer.PriceCategory) && scheme.StartsOn <= date && scheme.EndsOn >= date
			orderby scheme.DrugId == drug.Id descending, scheme.StartsOn descending
			select new { scheme.BuyQuantity, scheme.FreeQuantity }).FirstOrDefaultAsync(cancellationToken);
		decimal freeQuantity = ((anon == null) ? 0m : (decimal.Floor(paidQuantity / anon.BuyQuantity) * anon.FreeQuantity));
		return new WholesalePriceQuote(baseRate, discountPercent, unitRate, freeQuantity, mrp);
	}

	public async Task<IReadOnlyList<WholesaleRateHistoryRow>> GetLastRatesAsync(Guid customerId, Guid drugId, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await (from rate in unitOfWork.Context.WholesaleRateHistory.AsNoTracking()
			join invoice in unitOfWork.Context.WholesaleInvoices.AsNoTracking() on rate.InvoiceId equals invoice.Id
			where rate.CustomerId == customerId && rate.DrugId == drugId
			orderby rate.SoldAtUtc descending
			select new WholesaleRateHistoryRow(rate.SoldAtUtc, invoice.InvoiceNo, rate.UnitRate, rate.Quantity)).Take(5).ToListAsync(cancellationToken);
	}
}
