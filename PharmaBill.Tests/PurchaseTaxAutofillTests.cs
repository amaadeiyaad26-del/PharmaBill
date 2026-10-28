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

// Purchases / Inward bills, "Add item": choosing a medicine fills HSN Code and GST % from its master record, never keeps
// the last medicine's, takes a drug bank medicine's from same-salt medicines when they agree, and lets the pharmacist
// overtype either box for this invoice.
public sealed class PurchaseTaxAutofillTests
{
    [Fact]
    public void ChoosingAMedicine_FillsItsHsnAndGst_AndTheBoxesStayEditable()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using Inward shop = await Inward.OpenAsync();
            Window host = await shop.ShowAsync();
            PurchasePageViewModel page = shop.Page;
            TextBox hsn = Ui.Named<TextBox>(host, "HSN Code");
            TextBox gst = Ui.Single<TextBox>(host, box => box.Name == "PurchaseNewItemGst");

            // 1. From the medicine master, with a note saying so.
            await shop.ChooseAsync(host, "Dolo", "Dolo 650");
            Assert.Equal(("30049099", "12"), (hsn.Text, gst.Text));
            Assert.StartsWith("HSN 30049099 · GST 12% from the medicine master", Source(host));

            // 2. The next medicine has no tax on record: Dolo's values are not kept. Its HSN comes from its dosage form
            //    (a syrup is a medicament, 3004) with the reason; its rate is never guessed, so the GST box stays empty.
            await shop.ChooseAsync(host, "Old Cough", "Old Cough Syrup");
            Assert.Equal(("3004", ""), (hsn.Text, gst.Text));
            Assert.StartsWith("Syrup → 3004, medicaments in measured doses or retail packs", Source(host));

            // 3. A drug bank medicine carries no tax; Celin 500 has the same salt in the master: its HSN is offered, and its
            //    rate as "same as last time", with where it came from and the date it was recorded.
            await shop.ChooseAsync(host, "Limcee", "Limcee 500 Chewable");
            Assert.Equal(("30045010", "12"), (hsn.Text, gst.Text));
            Assert.StartsWith("Same HSN as Celin 500, the same salt in your medicine master. Same as last time: 12%, from Celin 500 (same salt), ", Source(host));
            Assert.Contains(PharmaBill.Core.BusinessClock.Today.ToString("dd-MMM-yyyy"), Source(host));

            // 4. Overtyping: this invoice bills Dolo at 5%. The line takes 5%; the master keeps 12%.
            await shop.ChooseAsync(host, "Dolo", "Dolo 650");
            Ui.Type(gst, "5");
            Assert.StartsWith("Changed from the master's HSN 30049099 · GST 12%", Source(host));
            Ui.Type(Ui.Single<TextBox>(host, box => box.Name == "PurchaseNewItemBatch"), "DL-01");
            Ui.Type(Ui.Single<TextBox>(host, box => box.Name == "PurchaseNewItemExpiry"), "10/2028");
            Ui.Type(Ui.Single<TextBox>(host, box => box.Name == "PurchaseNewItemQuantity"), "2");
            Ui.Type(Ui.Single<TextBox>(host, box => box.Name == "PurchaseNewItemMrp"), "30");
            Ui.Type(Ui.Single<TextBox>(host, box => box.Name == "PurchaseNewItemRate"), "20");
            await Ui.PressAsync(Ui.Button(host, "Add to bill"));
            Assert.True(page.NewItemError.Length == 0, page.NewItemError);
            Assert.Equal(5m, Assert.Single(page.Lines).GstRate);
            Drug master = await shop.Database.Context.Drugs.AsNoTracking().SingleAsync(drug => drug.Name == "Dolo 650");
            Assert.Equal(("30049099", (decimal?)12m), (master.HsnCode, master.GstRate));
            bindings.AssertNone();
            host.Close();
        });
    }

    [Fact]
    public async Task SameSaltSuggestion_OnlyWhenTheMasterAgrees()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        CatalogMedicine Bank(string name) => new() { SourceId = name, Name = name, CompositionKey = "vitamin c 500mg" };
        CatalogMedicine celinBank = Bank("Celin 500 Tablet"), limcee = Bank("Limcee 500"), vitceeBank = Bank("Vitcee 500");
        database.Context.AddRange(celinBank, limcee, vitceeBank);
        await database.Context.SaveChangesAsync();
        database.Context.Add(new Drug { Name = "Celin 500", IsActive = true, CatalogMedicineId = celinBank.Id, HsnCode = "30045010", GstRate = 12m });
        await database.Context.SaveChangesAsync();
        var service = new ProductTaxService(database.Context);

        SameSaltTax agreed = await service.SuggestFromSameSaltAsync(limcee.Id);
        Assert.True(agreed.HasSuggestion);
        Assert.Equal(("30045010", (decimal?)12m, "Celin 500"), (agreed.HsnCode, agreed.GstRate, agreed.ExampleName));

        // Two same-salt medicines on different rates: nothing is guessed.
        database.Context.Add(new Drug { Name = "Vitcee 500", IsActive = true, CatalogMedicineId = vitceeBank.Id, HsnCode = "30045010", GstRate = 5m });
        await database.Context.SaveChangesAsync();
        SameSaltTax split = await service.SuggestFromSameSaltAsync(limcee.Id);
        Assert.False(split.HasSuggestion);
        Assert.True(split.Disagree);
    }

    private static string Source(Window host) => Ui.Named<TextBlock>(host, "HSN and GST source").Text;

    private sealed class Inward : IAsyncDisposable
    {
        private ServiceProvider _services = null!;

        private Inward(DatabaseTestContext database) => Database = database;

        public DatabaseTestContext Database { get; }
        public PurchasePageViewModel Page { get; private set; } = null!;

        public static async Task<Inward> OpenAsync()
        {
            var database = await DatabaseTestContext.CreateAsync();
            var shop = new Inward(database);
            E2EHealthSeed.Catalog catalog = await E2EHealthSeed.SeedAsync(database.Context);
            var doloBank = new CatalogMedicine { SourceId = "D-1", Name = "Dolo 650 Tablet", CompositionKey = "paracetamol 650mg dolo", ShortComposition1 = "Paracetamol (650mg)" };
            var celinBank = new CatalogMedicine { SourceId = "C-1", Name = "Celin 500 Tablet", CompositionKey = "vitamin c 500mg", ShortComposition1 = "Vitamin C (500mg)" };
            var limcee = new CatalogMedicine { SourceId = "L-1", Name = "Limcee 500 Chewable", CompositionKey = "vitamin c 500mg", ShortComposition1 = "Vitamin C (500mg)", PackSizeLabel = "15 Tablets / Strip" };
            database.Context.AddRange(doloBank, celinBank, limcee);
            await database.Context.SaveChangesAsync();
            database.Context.AddRange(
                new Drug { Name = "Dolo 650", IsActive = true, CatalogMedicineId = doloBank.Id, HsnCode = "30049099", GstRate = 12m },
                new Drug { Name = "Celin 500", IsActive = true, CatalogMedicineId = celinBank.Id, HsnCode = "30045010", GstRate = 12m },
                new Drug { Name = "Old Cough Syrup", IsActive = true });
            await database.Context.SaveChangesAsync();
            await new CatalogImportService(database.Context).EnsureFtsIndexAsync();
            await database.Context.Database.ExecuteSqlRawAsync("INSERT INTO CatalogMedicineFts (MedicineId, Name, Composition, Manufacturer) VALUES ({0}, {1}, {2}, '')",
                limcee.Id.ToString("N"), limcee.Name, limcee.ShortComposition1!);
            var allowed = new Allowed();
            // Each scope its own context, as in the app: the page searches and looks up defaults on background threads.
            shop._services = new ServiceCollection()
                .AddSingleton(database.Storage)
                .AddSingleton(database.BranchSettings)
                .AddSingleton<IEntitlementService>(allowed)
                .AddSingleton<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>()
                .AddScoped(_ => database.OpenContext())
                .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
                .AddScoped<NumberSeriesService>()
                .AddScoped<StorageLocationService>()
                .AddScoped<BranchService>()
                .AddScoped<CatalogSearchService>()
                .AddScoped<PurchaseService>()
                .AddScoped<AddStockService>()
                .BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(catalog.Admin);
            shop.Page = new PurchasePageViewModel(shop._services.GetRequiredService<IServiceScopeFactory>(), session, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            await shop.Page.LoadAsync();
            await Ui.UntilAsync(() => shop.Page.IsMedicineListReady, "the medicine list to load");
            shop.Page.IsAddItemOpen = true;
            return shop;
        }

        public async Task<Window> ShowAsync()
        {
            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = Page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(PurchasePageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            return host;
        }

        // Types in the medicine box and picks the medicine as Enter or a click on the list does, then lets the
        // background look-ups (master record, same salt, last purchase) finish.
        public async Task ChooseAsync(Window host, string typed, string name)
        {
            Ui.Type(Ui.Single<TextBox>(host, box => box.Name == "PurchaseMedicineSearchBox"), typed);
            await Ui.UntilAsync(() => Page.MedicinePickerItems.Any(item => item.DisplayTitle == name), "\"" + name + "\" in the list");
            Page.SelectMedicineSuggestion(Page.MedicinePickerItems.First(item => item.DisplayTitle == name));
            await Ui.UntilAsync(() => Page.HasTaxSourceHint, "the HSN and GST note");
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
