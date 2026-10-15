using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class ProductTaxTests
{
    [Theory]
    [InlineData(null, null, false, false)]
    [InlineData("3004", null, false, false)]
    [InlineData("3004", 0, false, false)]
    [InlineData(null, 12, false, false)]
    [InlineData("  ", 12, false, false)]
    [InlineData("3004", 12, false, true)]
    [InlineData("3004", null, true, true)]
    [InlineData("3004", 0, true, true)]
    public void Known_TreatsMissingAndUnmarkedZeroAsUnknown(string? hsn, int? rate, bool nilRated, bool known)
    {
        Assert.Equal(known, ProductTax.Known(hsn, rate, nilRated) != null);
        Assert.Equal(known, ProductTax.DescribeMissing(hsn, rate, nilRated) == null);
    }

    [Fact]
    public void Known_NilRatedIsBilledAtZero()
    {
        ProductTaxDetails details = ProductTax.Known("3004", null, true)!;
        Assert.Equal(0m, details.GstRate);
        Assert.True(details.IsNilRated);
    }

    [Theory]
    [InlineData(null, 12, false, "Enter the HSN code")]
    [InlineData("30A4", 12, false, "4, 6 or 8 digits")]
    [InlineData("300", 12, false, "4, 6 or 8 digits")]
    [InlineData("3004", null, false, "Enter the GST rate")]
    [InlineData("3004", 0, false, "Nil-rated")]
    [InlineData("3004", 101, false, "between 0 and 100")]
    [InlineData("3004", 5, true, "Nil-rated product is billed at 0%")]
    public void TryCreate_RejectsIncompleteEntries(string? hsn, int? rate, bool nilRated, string expected)
    {
        Assert.False(ProductTax.TryCreate(hsn, rate, nilRated, out ProductTaxDetails? details, out string? error));
        Assert.Null(details);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void TryCreate_AcceptsRateOrExplicitNilRated_AndStripsSpacesFromHsn()
    {
        Assert.True(ProductTax.TryCreate(" 3004 90 99", 12m, false, out ProductTaxDetails? rated, out _));
        Assert.Equal(new ProductTaxDetails("30049099", 12m, false), rated);
        Assert.True(ProductTax.TryCreate("300490", null, true, out ProductTaxDetails? nil, out _));
        Assert.Equal(new ProductTaxDetails("300490", 0m, true), nil);
    }

    [Fact]
    public async Task MissingTaxReport_ListsUnknownRatesAndHsn_NotNilRatedOrInactiveProducts()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.Drugs.AddRange(
            new Drug { Name = "Complete", HsnCode = "3004", GstRate = 12m },
            new Drug { Name = "Nil rated", HsnCode = "3004", GstRate = 0m, IsNilRated = true },
            new Drug { Name = "Zero not marked", HsnCode = "3004", GstRate = 0m },
            new Drug { Name = "No rate", HsnCode = "3004" },
            new Drug { Name = "No HSN", GstRate = 5m },
            new Drug { Name = "Neither" },
            new Drug { Name = "Inactive", IsActive = false });
        await database.Context.SaveChangesAsync();

        IReadOnlyList<ProductMissingTaxRow> rows = await new ProductTaxService(database.Context).GetProductsMissingTaxAsync();

        Assert.Equal(new[] { "Neither", "No HSN", "No rate", "Zero not marked" }, rows.Select(row => row.Name));
        Assert.Equal("HSN code and GST rate", rows.Single(row => row.Name == "Neither").Missing);
        Assert.Equal("HSN code", rows.Single(row => row.Name == "No HSN").Missing);
        Assert.Equal("GST rate", rows.Single(row => row.Name == "Zero not marked").Missing);
    }

    [Fact]
    public async Task SetMany_SavesAllAndAudits_OrNothingWhenAnyEntryIsInvalid()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var first = new Drug { Name = "First" };
        var second = new Drug { Name = "Second", HsnCode = "3004" };
        database.Context.Drugs.AddRange(first, second);
        await database.Context.SaveChangesAsync();
        var service = new ProductTaxService(database.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetManyAsync(
            [new ProductTaxUpdate(first.Id, "3004", 12m, false), new ProductTaxUpdate(second.Id, "3004", 0m, false)], null, "test"));
        database.Context.ChangeTracker.Clear();
        Assert.Null((await database.Context.Drugs.SingleAsync(d => d.Id == first.Id)).GstRate);

        await service.SetManyAsync(
            [new ProductTaxUpdate(first.Id, "3004", 12m, false), new ProductTaxUpdate(second.Id, "3004", null, true)], null, "test");

        database.Context.ChangeTracker.Clear();
        Drug savedFirst = await database.Context.Drugs.SingleAsync(d => d.Id == first.Id);
        Drug savedSecond = await database.Context.Drugs.SingleAsync(d => d.Id == second.Id);
        Assert.Equal(("3004", 12m, false), (savedFirst.HsnCode, savedFirst.GstRate, savedFirst.IsNilRated));
        Assert.Equal(("3004", 0m, true), (savedSecond.HsnCode, savedSecond.GstRate, savedSecond.IsNilRated));
        Assert.Equal(2, await database.Context.AuditLogs.CountAsync(log => log.Action == "ProductTaxDetailsSet"));
        Assert.Empty(await service.GetProductsMissingTaxAsync());
    }

    [Theory]
    [InlineData(null, 12, false)]
    [InlineData("3004", null, false)]
    [InlineData("3004", 0, false)]
    public async Task CustomMedicine_CannotBeSavedWithoutHsnAndGst(string? hsn, int? rate, bool nilRated)
    {
        await using var database = await DatabaseTestContext.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CustomMedicineService(database.Context).SaveAsync(
            new CustomMedicineRequest("New brand", null, null, null, hsn, rate, 50m, 30m, IsNilRated: nilRated), null));

        Assert.Empty(await database.Context.Drugs.ToListAsync());
    }

    [Fact]
    public async Task CustomMedicine_EditAlsoRequiresBoth_AndStoresNilRated()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var service = new CustomMedicineService(database.Context);
        CustomMedicineResult created = await service.SaveAsync(new CustomMedicineRequest("Brand", null, null, null, "3004", 12m, 50m, 30m), null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(new CustomMedicineRequest("Brand", null, null, null, null, 12m, 50m, 30m), null));

        await service.SaveAsync(new CustomMedicineRequest("Brand", null, null, null, "3004", null, 50m, 30m, IsNilRated: true), null);
        database.Context.ChangeTracker.Clear();
        Drug drug = await database.Context.Drugs.SingleAsync(d => d.Id == created.DrugId);
        Assert.True(drug.IsNilRated);
        Assert.Equal(0m, drug.GstRate);
    }
}
