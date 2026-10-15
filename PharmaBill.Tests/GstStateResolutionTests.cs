using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class GstStateResolutionTests
{
    [Theory]
    [InlineData("J&K", "01")]
    [InlineData("Jammu & Kashmir", "01")]
    [InlineData("jammu and kashmir", "01")]
    [InlineData("  Tamil   Nadu ", "33")]
    [InlineData("Orissa", "21")]
    [InlineData("29", "29")]
    public void TryResolve_NormalisesNamesAndCodes(string state, string expected)
    {
        Assert.True(IndianGstStateCodes.TryResolve(state, null, out string? code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Atlantis", null)]
    [InlineData("99", null)]
    [InlineData(null, "XX1234")]
    [InlineData("Atlantis", "29ABCDE1234F1Z5")]
    public void TryResolve_ReportsUnresolved(string? state, string? gstin)
    {
        Assert.False(IndianGstStateCodes.TryResolve(state, gstin, out _));
    }

    [Fact]
    public void TryResolve_BlankStateFallsBackToGstinPrefix()
    {
        Assert.True(IndianGstStateCodes.TryResolve("", "29ABCDE1234F1Z5", out string? code));
        Assert.Equal("29", code);
        Assert.Equal("Karnataka", IndianGstStateCodes.StateNameFromGstin("29ABCDE1234F1Z5"));
    }

    [Fact]
    public void Resolve_StillReturns97ForReportsWhenUnknown()
    {
        Assert.Equal("97", IndianGstStateCodes.Resolve(null, null));
    }

    [Fact]
    public async Task WholesaleCustomer_StateIsTakenFromGstinWhenBlank()
    {
        await using var database = await CreateAsync(BusinessMode.Wholesaler);
        var customer = NewCustomer(state: null, gstin: "29ABCDE1234F1Z5");

        await Service(database).SaveAsync(customer, [], [], Guid.NewGuid());

        Assert.Equal("Karnataka", (await database.Context.Customers.SingleAsync()).State);
    }

    [Fact]
    public async Task WholesaleCustomer_EnteredStateIsStoredAsCanonicalName()
    {
        await using var database = await CreateAsync(BusinessMode.Wholesaler);

        await Service(database).SaveAsync(NewCustomer(state: "j&k", gstin: null), [], [], Guid.NewGuid());

        Assert.Equal("Jammu and Kashmir", (await database.Context.Customers.SingleAsync()).State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Atlantis")]
    public async Task WholesaleCustomer_WithoutGstinMustHaveRecognisedState(string? state)
    {
        await using var database = await CreateAsync(BusinessMode.Wholesaler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(database).SaveAsync(NewCustomer(state, gstin: null), [], [], Guid.NewGuid()));
        Assert.Empty(await database.Context.Customers.ToListAsync());
    }

    [Fact]
    public async Task RetailModeCustomer_StateStaysOptionalFreeText()
    {
        await using var database = await CreateAsync(BusinessMode.Retail);

        await Service(database).SaveAsync(NewCustomer(state: "North area", gstin: null), [], [], Guid.NewGuid());

        Assert.Equal("North area", (await database.Context.Customers.SingleAsync()).State);
    }

    private static async Task<DatabaseTestContext> CreateAsync(BusinessMode mode)
    {
        var database = await DatabaseTestContext.CreateAsync();
        database.Context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Test pharmacy", BusinessMode = mode });
        await database.Context.SaveChangesAsync();
        return database;
    }

    private static WholesaleCustomerService Service(DatabaseTestContext database) =>
        new(new UnitOfWork(database.Context));

    private static Customer NewCustomer(string? state, string? gstin) => new()
    {
        Name = "Buyer " + Guid.NewGuid().ToString("N")[..6],
        BuyerType = "Distributor",
        State = state,
        Gstin = gstin
    };
}
