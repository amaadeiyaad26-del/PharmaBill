using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class BuyerTypeRemappingTests
{
    [Theory]
    [InlineData("Wholesaler", "Distributor")]
    [InlineData(" stockist ", "Distributor")]
    [InlineData("STOCKIST", "Distributor")]
    [InlineData("Distributor / Wholesaler", "Distributor")]
    [InlineData("Distributor/Wholesaler", "Distributor")]
    [InlineData("Distributor", "Distributor")]
    [InlineData("Retailer", "Retailer")]
    [InlineData("Other", "Other")]
    public void Canonical_MapsSynonymsToDistributor(string input, string expected)
    {
        Assert.Equal(expected, BuyerTypes.Canonical(input));
    }

    [Fact]
    public void MergeAliasRules_KeepsAConfiguredDistributorRuleAndRecordsWhatWasDropped()
    {
        string json = JsonSerializer.Serialize(new Dictionary<string, string[]> { ["Distributor"] = ["20B"], ["Wholesaler"] = ["20B", "21B"] });

        (string merged, IReadOnlyList<string> changes) = BuyerLicenceRules.MergeAliasRules(json);

        var rules = BuyerLicenceRules.Parse(merged);
        Assert.False(rules.ContainsKey("Wholesaler"));
        Assert.Equal(["20B"], rules["Distributor"]);
        Assert.Contains("'Wholesaler' (20B, 21B) removed", Assert.Single(changes));
    }

    [Fact]
    public async Task SavingAStockist_StoresDistributorAndAuditsTheRemap()
    {
        await using var database = await CreateAsync();
        Guid userId = Guid.NewGuid();
        var customer = new Customer { Name = "Valley Stockist", BuyerType = "Stockist", Gstin = TestGstin.From("01") };

        await Service(database).SaveAsync(customer, [], [], userId);

        Assert.Equal("Distributor", (await database.Context.Customers.SingleAsync()).BuyerType);
        AuditLog audit = Assert.Single(await database.Context.AuditLogs.Where(log => log.Action == BuyerTypeRemapping.CustomerRemapAction).ToListAsync());
        Assert.Equal(userId, audit.UserId);
        Assert.Equal(customer.Id, audit.EntityId);
        Assert.Contains("'Stockist' stored as 'Distributor'", audit.Details);
    }

    [Fact]
    public async Task PickingTheDistributorLabel_IsNotLoggedAsARemap()
    {
        await using var database = await CreateAsync();

        await Service(database).SaveAsync(new Customer { Name = "Label pick", BuyerType = BuyerTypes.DistributorLabel, Gstin = TestGstin.From("01") }, [], [], Guid.NewGuid());

        Assert.Equal("Distributor", (await database.Context.Customers.SingleAsync()).BuyerType);
        Assert.Empty(await database.Context.AuditLogs.Where(log => log.Action == BuyerTypeRemapping.CustomerRemapAction).ToListAsync());
    }

    [Fact]
    public async Task ImportingAWholesaler_StoresDistributorAndAuditsTheRemap()
    {
        await using var database = await CreateAsync();
        var import = new WholesaleCustomerImportRecord(new Customer { Name = "Imported Wholesaler", BuyerType = "Wholesaler", Gstin = TestGstin.From("01") }, []);

        var result = await Service(database).ImportAsync([import], [], Guid.NewGuid());

        Assert.Equal(1, result.Imported);
        Assert.Equal("Distributor", (await database.Context.Customers.SingleAsync()).BuyerType);
        Assert.Single(await database.Context.AuditLogs.Where(log => log.Action == BuyerTypeRemapping.CustomerRemapAction).ToListAsync());
    }

    [Fact]
    public async Task Startup_MergesExistingSynonymsAndAuditsEachRemapOnce()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.PharmacyProfiles.Add(new PharmacyProfile
        {
            Name = "Legacy",
            BusinessMode = BusinessMode.Wholesaler,
            WholesaleBuyerLicenceRulesJson = JsonSerializer.Serialize(new Dictionary<string, string[]> { ["Wholesaler"] = ["20B"] })
        });
        var legacy = new Customer { Name = "Old Wholesaler", BuyerType = "Wholesaler" };
        database.Context.Customers.Add(legacy);
        await database.Context.SaveChangesAsync();

        await BuyerTypeRemapping.NormalizeExistingAsync(database.Context);
        Assert.Equal(0, await BuyerTypeRemapping.NormalizeExistingAsync(database.Context));

        Assert.Equal("Distributor", (await database.Context.Customers.AsNoTracking().SingleAsync()).BuyerType);
        var rules = BuyerLicenceRules.Parse((await database.Context.PharmacyProfiles.AsNoTracking().SingleAsync()).WholesaleBuyerLicenceRulesJson);
        Assert.Equal(["20B"], rules["Distributor"]);
        Assert.False(rules.ContainsKey("Wholesaler"));
        var audits = await database.Context.AuditLogs.AsNoTracking().ToListAsync();
        AuditLog customerAudit = Assert.Single(audits, log => log.Action == BuyerTypeRemapping.CustomerRemapAction);
        Assert.Equal(legacy.Id, customerAudit.EntityId);
        Assert.Equal("System", customerAudit.AuthorizedBy);
        Assert.Contains("moved to 'Distributor'", Assert.Single(audits, log => log.Action == BuyerTypeRemapping.RuleRemapAction).Details);
    }

    private static async Task<DatabaseTestContext> CreateAsync()
    {
        var database = await DatabaseTestContext.CreateAsync();
        database.Context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Remap pharmacy", BusinessMode = BusinessMode.Wholesaler });
        await database.Context.SaveChangesAsync();
        return database;
    }

    private static WholesaleCustomerService Service(DatabaseTestContext database) => new(new UnitOfWork(database.Context));
}
