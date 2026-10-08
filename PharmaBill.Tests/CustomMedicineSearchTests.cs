using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class CustomMedicineSearchTests
{
	[Fact]
	public async Task SaveAsync_IndexesCustomMedicineForBrandAndGenericSearch()
	{
		await using DatabaseTestContext database = await DatabaseTestContext.CreateAsync();
		CustomMedicineService service = new CustomMedicineService(database.Context);
		CustomMedicineResult saved = await service.SaveAsync(
			new CustomMedicineRequest(
				"Zyloquin 250",
				"Cefquinome",
				"Acme Labs",
				"Strip of 10",
				"3004",
				12m,
				48m,
				30m),
			userId: null);

		var medicine = await database.Context.CatalogMedicines.SingleAsync(item => item.Id == saved.CatalogMedicineId);
		Assert.True(medicine.IsCustom);
		Assert.StartsWith("custom:", medicine.SourceId);
		Assert.Equal(saved.DrugId, (await database.Context.Drugs.SingleAsync(item => item.Name == "Zyloquin 250")).Id);
		Assert.Equal(saved.CatalogMedicineId, (await database.Context.Drugs.SingleAsync(item => item.Id == saved.DrugId)).CatalogMedicineId);

		MedicineSearchResults byBrand = await database.CreateCatalogSearch().SearchAsync("Zyloquin");
		Assert.Contains(byBrand.FromCatalog, item => item.Name == "Zyloquin 250" && item.DrugId == saved.DrugId);

		MedicineSearchResults byGeneric = await database.CreateCatalogSearch().SearchAsync("Cefquinome");
		Assert.Contains(byGeneric.FromCatalog, item => item.DrugId == saved.DrugId && item.CatalogMedicineId == saved.CatalogMedicineId);

		MedicineSearchResults byCompany = await database.CreateCatalogSearch().SearchAsync("Acme");
		Assert.Contains(byCompany.FromCatalog, item => item.DrugId == saved.DrugId);
	}
}
