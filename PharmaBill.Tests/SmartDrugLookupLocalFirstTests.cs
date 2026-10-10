using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using Xunit;

namespace PharmaBill.Tests;

public sealed class SmartDrugLookupLocalFirstTests
{
    [Fact]
    public async Task Lookup_AnswersFromDrugBankWithoutCallingOnline()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.CatalogMedicines.Add(new CatalogMedicine
        {
            Name = "Corex DX Syrup",
            GenericName = "Dextromethorphan",
            CompositionKey = "dextromethorphan"
        });
        await database.Context.SaveChangesAsync();
        var provider = new ServiceCollection()
            .AddSingleton(database.Context)
            .BuildServiceProvider();
        // No API key is configured, so any online attempt would fail. A local answer proves the online step was skipped.
        var lookup = new SmartDrugLookupService(new HttpClient(), () => null, provider.GetRequiredService<IServiceScopeFactory>());

        var outcome = await lookup.LookupAsync("corex");

        Assert.NotNull(outcome.Suggestion);
        Assert.Equal("Corex DX Syrup", outcome.Suggestion!.BrandName);
        Assert.Contains("local medicine list", outcome.Message);
    }
}
