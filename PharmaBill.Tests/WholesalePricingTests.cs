using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class WholesalePricingTests
{
    [Fact]
    public async Task PackLevels_RequireUniqueLevelsAndBaseUnitConversion()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "Pack medicine" };
        database.Context.Drugs.Add(drug);
        await database.Context.SaveChangesAsync();
        var service = new WholesalePricingService(new UnitOfWork(database.Context));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SavePackLevelsAsync(drug.Id,
        [
            new DrugPackLevel { Level = PackLevel.Unit, Label = "tablet", UnitsPerPack = 2m },
            new DrugPackLevel { Level = PackLevel.Strip, Label = "strip", UnitsPerPack = 10m }
        ]));

        await service.SavePackLevelsAsync(drug.Id,
        [
            new DrugPackLevel { Level = PackLevel.Unit, Label = "tablet", UnitsPerPack = 1m },
            new DrugPackLevel { Level = PackLevel.Strip, Label = "strip of 10", UnitsPerPack = 10m },
            new DrugPackLevel { Level = PackLevel.Box, Label = "box of 10 strips", UnitsPerPack = 100m },
            new DrugPackLevel { Level = PackLevel.Carton, Label = "carton", UnitsPerPack = 1000m }
        ]);

        Assert.Equal(4, await database.Context.DrugPackLevels.CountAsync());
    }

    [Fact]
    public async Task WholesalePrice_UsesCategoryRateDiscountDatedSchemeAndRespectsMrp()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "Rate medicine", Mrp = 100m };
        var batch = new Batch { DrugId = drug.Id, BatchNo = "P-1", PurchasePrice = 50m, Ptr = 80m, Pts = 90m, Mrp = 100m };
        var customer = new Customer { Name = "Buyer", BuyerType = "Retailer", PriceCategory = "A" };
        database.Context.AddRange(drug, batch, customer);
        database.Context.CustomerCategoryPrices.Add(new CustomerCategoryPrice
        {
            DrugId = drug.Id,
            PriceCategory = "A",
            UnitPrice = 95m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = new DateOnly(2026, 12, 31)
        });
        database.Context.TradeDiscounts.Add(new TradeDiscount
        {
            PriceCategory = "A",
            DiscountPercent = 10m,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            EffectiveTo = new DateOnly(2026, 12, 31)
        });
        database.Context.WholesaleSchemes.Add(new WholesaleScheme
        {
            DrugId = drug.Id,
            PriceCategory = "A",
            BuyQuantity = 10m,
            FreeQuantity = 1m,
            StartsOn = new DateOnly(2026, 10, 1),
            EndsOn = new DateOnly(2026, 10, 31)
        });
        await database.Context.SaveChangesAsync();
        var service = new WholesalePricingService(new UnitOfWork(database.Context));

        var quote = await service.GetQuoteAsync(customer, drug, batch, 25m, new DateOnly(2026, 10, 3));

        Assert.Equal(95m, quote.BaseUnitRate);
        Assert.Equal(10m, quote.DiscountPercent);
        Assert.Equal(85.50m, quote.UnitRate);
        Assert.Equal(2m, quote.FreeQuantity);
        Assert.Equal(100m, quote.EffectiveMrp);
    }

    [Fact]
    public async Task WholesalePrice_BlocksCustomerPriceAboveMrp()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "Price-capped medicine" };
        var batch = new Batch { DrugId = drug.Id, BatchNo = "P-2", PurchasePrice = 10m, Mrp = 20m };
        var customer = new Customer { Name = "Buyer", BuyerType = "Retailer", PriceCategory = "B" };
        database.Context.AddRange(drug, batch, customer);
        await database.Context.SaveChangesAsync();
        var service = new WholesalePricingService(new UnitOfWork(database.Context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveCategoryPriceAsync(
            new CustomerCategoryPrice { DrugId = drug.Id, PriceCategory = "B", UnitPrice = 21m }));
        database.Context.CustomerCategoryPrices.Add(new CustomerCategoryPrice
        {
            DrugId = drug.Id,
            PriceCategory = "B",
            UnitPrice = 21m
        });
        await database.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetQuoteAsync(customer, drug, batch, 1m, DateOnly.FromDateTime(DateTime.Today)));
    }

    [Fact]
    public async Task BatchRates_UpdatesRatesWithinMrpCeilingAndAuditsChange()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "Audited rate medicine" };
        var batch = new Batch { DrugId = drug.Id, BatchNo = "RATE-1", PurchasePrice = 10m, Mrp = 20m };
        database.Context.AddRange(drug, batch);
        await database.Context.SaveChangesAsync();
        var service = new WholesalePricingService(new UnitOfWork(database.Context));

        await service.SaveBatchRatesAsync(batch.Id, new WholesaleBatchRate(11m, 16m, 18m, 20m), Guid.NewGuid());

        Assert.Equal(11m, batch.PurchasePrice);
        Assert.Equal(16m, batch.Ptr);
        Assert.Equal(18m, batch.Pts);
        Assert.Equal("WholesaleBatchRatesUpdated", Assert.Single(database.Context.AuditLogs).Action);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveBatchRatesAsync(batch.Id, new WholesaleBatchRate(11m, 21m, null, 20m), Guid.NewGuid()));
    }
}
