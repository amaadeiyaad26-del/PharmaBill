using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record WholesalePriceQuote(
    decimal BaseUnitRate,
    decimal DiscountPercent,
    decimal UnitRate,
    decimal FreeQuantity,
    decimal EffectiveMrp);

public sealed record WholesaleRateHistoryRow(
    DateTime SoldAtUtc,
    string InvoiceNo,
    decimal UnitRate,
    decimal Quantity);

public sealed record WholesaleBatchRate(
    decimal PurchaseRate,
    decimal? Ptr,
    decimal? Pts,
    decimal Mrp);

public sealed class WholesalePricingService(IUnitOfWork unitOfWork)
{
    public async Task SaveBatchRatesAsync(
        Guid batchId,
        WholesaleBatchRate rates,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (rates.PurchaseRate < 0 || rates.Ptr < 0 || rates.Pts < 0 || rates.Mrp <= 0 ||
            (rates.Ptr.HasValue && rates.Ptr > rates.Mrp) ||
            (rates.Pts.HasValue && rates.Pts > rates.Mrp))
        {
            throw new InvalidOperationException("Rates must be non-negative and PTR/PTS cannot exceed batch MRP.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var batch = await unitOfWork.Context.Batches.SingleAsync(item => item.Id == batchId, cancellationToken);
        batch.PurchasePrice = rates.PurchaseRate;
        batch.Ptr = rates.Ptr;
        batch.Pts = rates.Pts;
        batch.Mrp = rates.Mrp;
        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            ActionAtUtc = DateTime.UtcNow,
            Action = "WholesaleBatchRatesUpdated",
            EntityName = nameof(Batch),
            EntityId = batch.Id,
            Details = $"PTR {rates.Ptr}; PTS {rates.Pts}; MRP {rates.Mrp}."
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SavePackLevelsAsync(
        Guid drugId,
        IReadOnlyCollection<DrugPackLevel> packLevels,
        CancellationToken cancellationToken = default)
    {
        if (packLevels.Count == 0 ||
            packLevels.Any(level => level.UnitsPerPack <= 0 || string.IsNullOrWhiteSpace(level.Label)) ||
            packLevels.Select(level => level.Level).Distinct().Count() != packLevels.Count)
        {
            throw new InvalidOperationException("Pack levels need unique levels, labels, and positive base-unit conversions.");
        }

        if (packLevels.SingleOrDefault(level => level.Level == PackLevel.Unit)?.UnitsPerPack != 1m)
        {
            throw new InvalidOperationException("The unit pack level must convert to exactly one base unit.");
        }

        if (!await unitOfWork.Context.Drugs.AnyAsync(drug => drug.Id == drugId, cancellationToken))
        {
            throw new InvalidOperationException("The selected medicine does not exist.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var existing = await unitOfWork.Context.DrugPackLevels
            .Where(level => level.DrugId == drugId)
            .ToListAsync(cancellationToken);
        unitOfWork.Context.DrugPackLevels.RemoveRange(existing);
        foreach (var level in packLevels)
        {
            unitOfWork.Context.DrugPackLevels.Add(new DrugPackLevel
            {
                DrugId = drugId,
                Level = level.Level,
                Label = level.Label.Trim(),
                UnitsPerPack = level.UnitsPerPack
            });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveCategoryPriceAsync(
        CustomerCategoryPrice price,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(price.PriceCategory) || price.UnitPrice < 0 ||
            (price.EffectiveFrom.HasValue && price.EffectiveTo.HasValue &&
             price.EffectiveTo < price.EffectiveFrom))
        {
            throw new InvalidOperationException("Enter a valid category price and effective period.");
        }

        if (price.DrugId.HasValue)
        {
            var drug = await unitOfWork.Context.Drugs.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == price.DrugId, cancellationToken)
                ?? throw new InvalidOperationException("The selected medicine does not exist.");
            var mrp = await unitOfWork.Context.Batches.AsNoTracking()
                .Where(batch => batch.DrugId == drug.Id && batch.Mrp.HasValue)
                .Select(batch => (decimal?)batch.Mrp)
                .MinAsync(cancellationToken) ?? drug.Mrp;
            if (mrp.HasValue && price.UnitPrice > mrp.Value)
            {
                throw new InvalidOperationException("Customer category price cannot exceed the medicine's MRP.");
            }
        }

        unitOfWork.Context.CustomerCategoryPrices.Add(price);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveTradeDiscountAsync(
        TradeDiscount discount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(discount.PriceCategory) ||
            discount.DiscountPercent is < 0 or > 100 ||
            (discount.EffectiveFrom.HasValue && discount.EffectiveTo.HasValue &&
             discount.EffectiveTo < discount.EffectiveFrom))
        {
            throw new InvalidOperationException("Trade discount must be between 0 and 100 percent with a valid date range.");
        }

        unitOfWork.Context.TradeDiscounts.Add(discount);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveSchemeAsync(WholesaleScheme scheme, CancellationToken cancellationToken = default)
    {
        if (scheme.BuyQuantity <= 0 || scheme.FreeQuantity <= 0 || scheme.EndsOn < scheme.StartsOn)
        {
            throw new InvalidOperationException("A scheme needs positive buy/free quantities and a valid date range.");
        }

        unitOfWork.Context.WholesaleSchemes.Add(scheme);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<WholesalePriceQuote> GetQuoteAsync(
        Customer customer,
        Drug drug,
        Batch batch,
        decimal paidQuantity,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        if (paidQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paidQuantity), "Paid quantity must be positive.");
        }

        var mrp = batch.Mrp ?? drug.Mrp
            ?? throw new InvalidOperationException("Batch MRP is required to quote a wholesale price.");
        var categoryPrice = !string.IsNullOrWhiteSpace(customer.PriceCategory)
            ? await unitOfWork.Context.CustomerCategoryPrices.AsNoTracking()
                .Where(price => (!price.DrugId.HasValue || price.DrugId == drug.Id) &&
                                price.PriceCategory == customer.PriceCategory &&
                                (!price.EffectiveFrom.HasValue || price.EffectiveFrom.Value <= date) &&
                                (!price.EffectiveTo.HasValue || price.EffectiveTo.Value >= date))
                .OrderByDescending(price => price.EffectiveFrom)
                .ThenByDescending(price => price.DrugId == drug.Id)
                .Select(price => (decimal?)price.UnitPrice)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var baseRate = categoryPrice ?? batch.Pts ?? batch.Ptr ?? batch.PurchasePrice;
        var discountPercent = !string.IsNullOrWhiteSpace(customer.PriceCategory)
            ? await unitOfWork.Context.TradeDiscounts.AsNoTracking()
                .Where(discount => discount.PriceCategory == customer.PriceCategory &&
                                   (!discount.DrugId.HasValue || discount.DrugId == drug.Id) &&
                                   (!discount.EffectiveFrom.HasValue || discount.EffectiveFrom.Value <= date) &&
                                   (!discount.EffectiveTo.HasValue || discount.EffectiveTo.Value >= date))
                .OrderByDescending(discount => discount.DrugId == drug.Id)
                .ThenByDescending(discount => discount.EffectiveFrom)
                .Select(discount => (decimal?)discount.DiscountPercent)
                .FirstOrDefaultAsync(cancellationToken) ?? 0m
            : 0m;
        var unitRate = decimal.Round(baseRate * (1m - discountPercent / 100m), 2, MidpointRounding.AwayFromZero);
        if (unitRate > mrp)
        {
            throw new InvalidOperationException($"Wholesale price for {drug.Name} cannot exceed batch MRP.");
        }

        var freeQuantity = await unitOfWork.Context.WholesaleSchemes.AsNoTracking()
            .Where(scheme => (!scheme.DrugId.HasValue || scheme.DrugId == drug.Id) &&
                             (scheme.PriceCategory == null || scheme.PriceCategory == customer.PriceCategory) &&
                             scheme.StartsOn <= date && scheme.EndsOn >= date)
            .OrderByDescending(scheme => scheme.DrugId == drug.Id)
            .ThenByDescending(scheme => scheme.StartsOn)
            .Select(scheme => new { scheme.BuyQuantity, scheme.FreeQuantity })
            .FirstOrDefaultAsync(cancellationToken);
        var promotionalFreeQuantity = freeQuantity is null
            ? 0m
            : decimal.Floor(paidQuantity / freeQuantity.BuyQuantity) * freeQuantity.FreeQuantity;

        return new WholesalePriceQuote(baseRate, discountPercent, unitRate, promotionalFreeQuantity, mrp);
    }

    public async Task<IReadOnlyList<WholesaleRateHistoryRow>> GetLastRatesAsync(
        Guid customerId,
        Guid drugId,
        CancellationToken cancellationToken = default) =>
        await (from rate in unitOfWork.Context.WholesaleRateHistory.AsNoTracking()
               join invoice in unitOfWork.Context.WholesaleInvoices.AsNoTracking()
                   on rate.InvoiceId equals invoice.Id
               where rate.CustomerId == customerId && rate.DrugId == drugId
               orderby rate.SoldAtUtc descending
               select new WholesaleRateHistoryRow(
                   rate.SoldAtUtc,
                   invoice.InvoiceNo,
                   rate.UnitRate,
                   rate.Quantity))
            .Take(5)
            .ToListAsync(cancellationToken);
}
