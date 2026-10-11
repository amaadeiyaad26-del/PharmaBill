using PharmaBill.Core.Entities;
using Xunit;

namespace PharmaBill.Tests;

public sealed class CatalogSearchFallbackTests
{
    [Fact]
    public async Task Search_FindsDrugBankMedicineWhenFullTextIndexIsEmpty()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.CatalogMedicines.Add(new CatalogMedicine
        {
            Name = "Azee 500 Tablet",
            GenericName = "Azithromycin",
            CompositionKey = "azithromycin 500mg"
        });
        await database.Context.SaveChangesAsync();

        var results = await database.CreateCatalogSearch().SearchAsync("Azee");

        Assert.Contains(results.FromCatalog, medicine => medicine.Name == "Azee 500 Tablet");
    }
}
