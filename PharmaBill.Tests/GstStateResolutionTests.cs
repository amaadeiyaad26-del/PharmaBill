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
    public async Task RetailModeCustomer_StateIsOptionalAndAreaIsFreeText()
    {
        await using var database = await CreateAsync(BusinessMode.Retail);
        var customer = NewCustomer(state: null, gstin: null);
        customer.Area = "  North area ";

        await Service(database).SaveAsync(customer, [], [], Guid.NewGuid());

        var stored = await database.Context.Customers.SingleAsync();
        Assert.Null(stored.State);
        Assert.Equal("North area", stored.Area);
    }

    [Fact]
    public async Task RetailModeCustomer_StateMustBeARecognisedStateAndIsCanonicalised()
    {
        await using var database = await CreateAsync(BusinessMode.Retail);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Service(database).SaveAsync(NewCustomer(state: "SRINAGAR", gstin: null), [], [], Guid.NewGuid()));
        await Service(database).SaveAsync(NewCustomer(state: "j & k", gstin: null), [], [], Guid.NewGuid());

        Assert.Equal("Jammu and Kashmir", (await database.Context.Customers.SingleAsync()).State);
    }

    [Theory]
    [InlineData("SRINAGAR", null, null, "SRINAGAR")]
    [InlineData("jammu & kashmir", null, "Jammu and Kashmir", null)]
    [InlineData("29", "Bengaluru", "Karnataka", "Bengaluru")]
    [InlineData("SRINAGAR", "Lal Chowk", null, "Lal Chowk, SRINAGAR")]
    [InlineData("srinagar", "SRINAGAR", null, "SRINAGAR")]
    [InlineData(null, null, null, null)]
    public void CustomerStateArea_SplitsLegacyFreeText(string? state, string? area, string? expectedState, string? expectedArea)
    {
        Assert.Equal((expectedState, expectedArea), CustomerStateArea.Split(state, area));
    }

    [Fact]
    public async Task CustomerStateArea_NormalisesExistingRowsOnce()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var city = NewCustomer(state: "SRINAGAR", gstin: null);
        var lower = NewCustomer(state: "karnataka", gstin: null);
        var canonical = NewCustomer(state: "Kerala", gstin: null);
        var blank = NewCustomer(state: null, gstin: null);
        database.Context.Customers.AddRange(city, lower, canonical, blank);
        await database.Context.SaveChangesAsync();

        Assert.Equal(2, await CustomerStateArea.NormalizeExistingAsync(database.Context));
        Assert.Equal(0, await CustomerStateArea.NormalizeExistingAsync(database.Context));

        var rows = await database.Context.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id);
        Assert.Null(rows[city.Id].State);
        Assert.Equal("SRINAGAR", rows[city.Id].Area);
        Assert.Equal("Karnataka", rows[lower.Id].State);
        Assert.Equal("Kerala", rows[canonical.Id].State);
        Assert.Null(rows[blank.Id].State);
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
