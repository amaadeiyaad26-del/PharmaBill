using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class BuyerLicenceRulesTests
{
    [Fact]
    public void NewProfile_ShipsDefaultsForEveryBuyerType()
    {
        var rules = BuyerLicenceRules.Parse(new PharmacyProfile().WholesaleBuyerLicenceRulesJson);

        Assert.Equal(["20", "21"], BuyerLicenceRules.AllowedFor(rules, "Retailer"));
        Assert.Equal(["20B", "21B"], BuyerLicenceRules.AllowedFor(rules, "wholesaler"));
        Assert.Equal(["20B", "21B"], BuyerLicenceRules.AllowedFor(rules, "Distributor"));
        Assert.All(BuyerLicenceRules.Defaults.Keys, buyerType => Assert.NotEmpty(BuyerLicenceRules.AllowedFor(rules, buyerType)));
    }

    [Fact]
    public void AddMissingDefaults_FillsGapsWithoutChangingConfiguredRules()
    {
        string legacy = JsonSerializer.Serialize(new Dictionary<string, string[]> { ["retailer"] = ["20"], ["Distributor"] = [] });

        (string json, bool changed) = BuyerLicenceRules.AddMissingDefaults(legacy);
        var rules = BuyerLicenceRules.Parse(json);

        Assert.True(changed);
        Assert.Equal(["20"], BuyerLicenceRules.AllowedFor(rules, "Retailer"));
        Assert.Equal(["20B", "21B"], BuyerLicenceRules.AllowedFor(rules, "Distributor"));
        Assert.False(BuyerLicenceRules.AddMissingDefaults(json).Changed);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    public void EmptyRules_ExplainWhereToSetThemWithATypicalValue(string json)
    {
        string? message = BuyerLicenceRules.MissingRuleMessage(BuyerLicenceRules.Parse(json), "Retailer");

        Assert.NotNull(message);
        Assert.Contains("blocked", message);
        Assert.Contains(BuyerLicenceRules.WhereToSet, message);
        Assert.Contains(BuyerLicenceRules.TypicalExample, message);
    }

    [Fact]
    public async Task SavingARule_ReplacesTheTickedSet()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Rules pharmacy", BusinessMode = BusinessMode.Wholesaler });
        await database.Context.SaveChangesAsync();
        var service = new WholesaleCustomerService(new UnitOfWork(database.Context));

        await service.SaveBuyerLicenceRulesAsync(new Dictionary<string, IReadOnlyCollection<string>> { ["Retailer"] = ["21"] });

        Assert.Equal(["21"], (await service.GetBuyerLicenceRulesAsync())["Retailer"]);
        Assert.Contains("20B", await service.GetLicenceTypesAsync());
    }

    [Fact]
    public async Task Startup_AddsDefaultsToAnExistingEmptyRuleSet()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Legacy pharmacy", WholesaleBuyerLicenceRulesJson = "{}" });
        await database.Context.SaveChangesAsync();

        await database.CreateDbInitializer().InitializeAsync();

        string json = (await database.Context.PharmacyProfiles.AsNoTracking().SingleAsync()).WholesaleBuyerLicenceRulesJson;
        Assert.Equal(["20", "21"], BuyerLicenceRules.AllowedFor(BuyerLicenceRules.Parse(json), "Retailer"));
    }
}
