using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

/// <summary>"29" => checksum-valid GSTIN for state 29; "29!" => same with a wrong check digit; else literal.</summary>
internal static class TestGstin
{
    public static string? From(string? spec)
    {
        if (spec == null || (spec.Length != 2 && !(spec.Length == 3 && spec[2] == '!')))
        {
            return spec;
        }
        string first14 = spec[..2] + "ABCDE1234F1Z";
        char check = GstinValidator.ComputeCheckDigit(first14);
        if (spec.Length == 3)
        {
            check = check == '0' ? '1' : '0';
        }
        return first14 + check;
    }
}

public sealed class GstStateResolutionTests
{
    [Fact]
    public void TestGstinHelper_ProducesValidAndInvalidGstins()
    {
        Assert.True(GstinValidator.IsValid(TestGstin.From("29")));
        Assert.False(GstinValidator.IsValid(TestGstin.From("29!")));
    }

    [Theory]
    [InlineData("J&K", "01")]
    [InlineData("Jammu & Kashmir", "01")]
    [InlineData("jammu and kashmir", "01")]
    [InlineData("  Tamil   Nadu ", "33")]
    [InlineData("Orissa", "21")]
    [InlineData("29", "29")]
    public void WithoutGstin_TypedNameIsNormalised(string state, string expected)
    {
        GstStateResolution resolution = IndianGstStateCodes.ResolveForTax(state, null);

        Assert.Equal(expected, resolution.Code);
        Assert.Equal(GstStateSource.StateName, resolution.Source);
        Assert.Null(resolution.Note);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("Atlantis", null)]
    [InlineData("99", null)]
    [InlineData(null, "XX1234")]
    [InlineData("Atlantis", "29!")]
    [InlineData(null, "29!")]
    public void NoValidGstinAndNoRecognisedName_IsUnresolved(string? state, string? gstin)
    {
        Assert.False(IndianGstStateCodes.ResolveForTax(state, TestGstin.From(gstin)).IsResolved);
    }

    [Fact]
    public void ValidGstin_IsSourceOfTruthWithNoteForUnrecognisedName()
    {
        GstStateResolution resolution = IndianGstStateCodes.ResolveForTax("Karnatak", TestGstin.From("29"));

        Assert.Equal("29", resolution.Code);
        Assert.Equal(GstStateSource.Gstin, resolution.Source);
        Assert.Equal("Using Karnataka (29) from the GSTIN; 'Karnatak' was not recognised.", resolution.Note);
    }

    [Fact]
    public void ValidGstin_IsSourceOfTruthWithNoteForDisagreeingName()
    {
        GstStateResolution resolution = IndianGstStateCodes.ResolveForTax("Tamil Nadu", TestGstin.From("29"));

        Assert.Equal("29", resolution.Code);
        Assert.Equal("Using Karnataka (29) from the GSTIN; the stored state 'Tamil Nadu' (33) disagrees.", resolution.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Karnataka")]
    [InlineData("karnataka")]
    [InlineData("29")]
    public void ValidGstin_AgreeingOrBlankName_HasNoNote(string? state)
    {
        GstStateResolution resolution = IndianGstStateCodes.ResolveForTax(state, TestGstin.From("29"));

        Assert.Equal("29", resolution.Code);
        Assert.Null(resolution.Note);
    }

    [Fact]
    public void InvalidGstin_FallsBackToTypedName()
    {
        GstStateResolution resolution = IndianGstStateCodes.ResolveForTax("Tamil Nadu", TestGstin.From("29!"));

        Assert.Equal("33", resolution.Code);
        Assert.Equal(GstStateSource.StateName, resolution.Source);
    }

    [Fact]
    public void Resolve_ForReportsFollowsGstinPrecedenceAndStillReturns97WhenUnknown()
    {
        Assert.Equal("29", IndianGstStateCodes.Resolve("Tamil Nadu", TestGstin.From("29")));
        Assert.Equal("97", IndianGstStateCodes.Resolve(null, null));
    }

    [Fact]
    public async Task WholesaleCustomer_StateIsTakenFromGstinWhenBlank()
    {
        await using var database = await CreateAsync(BusinessMode.Wholesaler);

        await Service(database).SaveAsync(NewCustomer(state: null, gstin: TestGstin.From("29")), [], [], Guid.NewGuid());

        Assert.Equal("Karnataka", (await database.Context.Customers.SingleAsync()).State);
    }

    [Fact]
    public async Task WholesaleCustomer_WithoutGstinTypedStateIsStoredAsCanonicalName()
    {
        await using var database = await CreateAsync(BusinessMode.Wholesaler);

        await Service(database).SaveAsync(NewCustomer(state: "j&k", gstin: null), [], [], Guid.NewGuid());

        Assert.Equal("Jammu and Kashmir", (await database.Context.Customers.SingleAsync()).State);
    }

    [Theory]
    [InlineData("Tamil Nadu")]
    [InlineData("Karnatak")]
    public async Task WholesaleCustomer_NameDisagreeingWithValidGstinIsKeptNotSilentlyFixed(string state)
    {
        await using var database = await CreateAsync(BusinessMode.Wholesaler);

        await Service(database).SaveAsync(NewCustomer(state, gstin: TestGstin.From("29")), [], [], Guid.NewGuid());

        Assert.Equal(state, (await database.Context.Customers.SingleAsync()).State);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Atlantis", null)]
    [InlineData("Atlantis", "29!")]
    public async Task WholesaleCustomer_WithoutValidGstinMustHaveRecognisedState(string? state, string? gstin)
    {
        await using var database = await CreateAsync(BusinessMode.Wholesaler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(database).SaveAsync(NewCustomer(state, TestGstin.From(gstin)), [], [], Guid.NewGuid()));
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
