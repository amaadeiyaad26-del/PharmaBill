using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Inward / Distributor Bills, "Add item": the HSN Code box sits on the row, so a medicine with no HSN on record (a Drug
// Bank pick, or an older medicine) gets one before the line is added, and it is saved to the medicine.
public sealed class PurchaseInwardHsnTests
{
    [Fact]
    public void HsnTypedOnTheRow_LetsTheLineIn_AndIsSavedToTheMedicine()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            var drugBank = new CatalogMedicine { SourceId = "L-1", Name = "Limcee 500 Chewable", CompositionKey = "vitamin c 500mg", ShortComposition1 = "Vitamin C (500mg)", PackSizeLabel = "15 Tablets / Strip" };
            var older = new Drug { Name = "Old Cough Syrup", IsActive = true };
            database.Context.AddRange(drugBank, older);
            await database.Context.SaveChangesAsync();

            var allowed = new Allowed();
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Context)
                .AddScoped(_ =>
                {
                    UnitOfWork uow = database.CreateUnitOfWork();
                    return new AddStockService(uow, database.CreatePurchaseService(allowed, uow), allowed, new NullLicenseRuntimeGuard());
                })
                .BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(catalog.Admin);
            var page = new PurchasePageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, null!, null!, null!, null!, null!, null!, null!, null!, null!) { IsAddItemOpen = true };

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1366, Height = 900 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(PurchasePageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            TextBox Box(string name) => Ui.Single<TextBox>(host, box => box.Name == name);
            TextBox hsn = Ui.Named<TextBox>(host, "HSN Code");
            Assert.True(Ui.Named<Border>(host, "Purchase steps").IsVisible);

            async Task FillAndAddAsync(string hsnCode)
            {
                Ui.Type(Box("PurchaseNewItemBatch"), "B-" + page.Lines.Count);
                Ui.Type(Box("PurchaseNewItemExpiry"), "10/2028");
                Ui.Type(Box("PurchaseNewItemQuantity"), "2");
                Ui.Type(Box("PurchaseNewItemMrp"), "30");
                Ui.Type(Box("PurchaseNewItemRate"), "20");
                Ui.Type(Box("PurchaseNewItemGst"), "12");
                Ui.Type(hsn, hsnCode);
                await Ui.PressAsync(Ui.Button(host, "Add to bill"));
            }

            // 1. A Drug Bank medicine not yet in the master: without an HSN it is refused, with one it is added and created.
            page.SelectMedicineSuggestion(PurchaseMedicinePickerItem.FromCatalogue(
                new MedicineSearchResult(drugBank.Id, drugBank.Name, "Vitamin C (500mg)", drugBank.CompositionKey, null, drugBank.PackSizeLabel, null, false, 0m, false), null, null));
            await Ui.SettleAsync();
            Assert.True(hsn.IsVisible, "the HSN Code box is on the row for a Drug Bank medicine");
            await FillAndAddAsync(string.Empty);
            Assert.Contains("HSN", page.NewItemError);
            Assert.Empty(page.Lines);
            await FillAndAddAsync("30049099");
            Assert.Equal(string.Empty, page.NewItemError);
            Drug created = await database.Context.Drugs.AsNoTracking().SingleAsync(drug => drug.CatalogMedicineId == drugBank.Id);
            Assert.Equal(("30049099", (decimal?)12m), (created.HsnCode, created.GstRate));
            Assert.Single(page.Lines);

            // 2. An older medicine with no HSN on record: the same box completes its master record.
            page.SelectMedicineSuggestion(PurchaseMedicinePickerItem.FromLocalDrug(older));
            await Ui.SettleAsync();
            await Ui.UntilAsync(() => hsn.Text == "3004", "the HSN from its dosage form (syrup)");
            await FillAndAddAsync(string.Empty);
            Assert.Contains("HSN", page.NewItemError);
            Assert.Single(page.Lines);
            await FillAndAddAsync("3004");
            Assert.Equal(string.Empty, page.NewItemError);
            Drug completed = await database.Context.Drugs.AsNoTracking().SingleAsync(drug => drug.Id == older.Id);
            Assert.Equal(("3004", (decimal?)12m), (completed.HsnCode, completed.GstRate));
            Assert.Equal(2, page.Lines.Count);

            // 3. A medicine with an HSN on record shows it; a corrected HSN typed here updates the medicine and is audited.
            Drug paracetamol = await database.Context.Drugs.AsNoTracking().SingleAsync(drug => drug.Id == catalog.Paracetamol650.Id);
            page.SelectMedicineSuggestion(PurchaseMedicinePickerItem.FromLocalDrug(paracetamol));
            await Ui.SettleAsync();
            Assert.Equal(paracetamol.HsnCode, hsn.Text);
            await FillAndAddAsync("30049011");
            Assert.Equal(string.Empty, page.NewItemError);
            Drug corrected = await database.Context.Drugs.AsNoTracking().SingleAsync(drug => drug.Id == paracetamol.Id);
            Assert.Equal(("30049011", paracetamol.GstRate), (corrected.HsnCode, corrected.GstRate));
            Assert.True(await database.Context.AuditLogs.AnyAsync(log => log.EntityId == paracetamol.Id && log.Details!.Contains("30049011")));
            Assert.Equal(3, page.Lines.Count);
            host.Close();
        });
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
