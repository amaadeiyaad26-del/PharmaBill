using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// The counter command bar on the retail billing screen, typed into and pressed: a misspelled medicine with two batches,
// a medicine only in the drug bank, and the hand-off to Purchases. The bill it builds is saved as any other bill.
public sealed class PosCommandBarTests
{
    [Fact]
    public void Command_PreviewsTheLine_AddsIt_AndTheBillSavesWithStockTaken()
    {
        Ui.Run(async () =>
        {
            await using Shop shop = await Shop.OpenAsync();
            // A second Paracetamol batch expiring later: the earliest-expiring one must come first.
            Batch later = await shop.AddBatchAsync(shop.Catalog.Paracetamol650, "PCM-LATE", BusinessClock.Today.AddYears(2), 5m);
            Window host = await shop.ShowBillingAsync();
            RetailBillingViewModel page = shop.Page;
            // An empty bill says how to start.
            Assert.True(Ui.Named<StackPanel>(host, "Empty bill hint").IsVisible);

            Ui.Type(Ui.Named<TextBox>(host, "Sale command"), "sold 2 strips of paracetmol to mr x");
            await PressEnterAsync(page);
            Assert.True(page.HasPosPreview, page.PosMessage);
            PosCommandPreview preview = page.PosPreview!;
            Assert.Equal("Paracetamol 650mg", preview.DrugName);
            Assert.Equal(2m, preview.PackQuantity);
            Assert.Equal("Mr X", preview.Customer);
            Assert.Contains("read as Paracetamol 650mg", Assert.Single(preview.Notes));
            Assert.True(preview.HasBatchChoice);
            Assert.Equal(shop.Catalog.ParacetamolBatch.BatchNo, preview.SelectedBatch.BatchNo);
            Assert.StartsWith("HSN 3004", preview.TaxSummary);
            Assert.Contains("CGST", preview.TaxSummary);
            Assert.True(Ui.Named<ComboBox>(host, "Batch").IsVisible);

            // Enter again adds it, to the earliest-expiring batch, and names the patient.
            await PressEnterAsync(page);
            RetailBillLineViewModel line = Assert.Single(page.BillItems);
            Assert.Equal((shop.Catalog.ParacetamolBatch.Id, 2m), (line.Choice.BatchId, line.Quantity));
            Assert.Equal("Mr X", page.PatientName);
            Assert.False(page.HasPosPreview);
            Assert.Equal(string.Empty, page.PosCommandText);
            Assert.StartsWith("Added Paracetamol 650mg", page.PosMessage);
            await Ui.SettleAsync();
            Assert.False(Ui.Named<StackPanel>(host, "Empty bill hint", visibleOnly: false).IsVisible);

            // The bill saves like any other: invoice numbered, stock taken from that batch.
            decimal before = await shop.OnHandAsync(shop.Catalog.ParacetamolBatch.Id);
            page.PatientPhone = "9876543210";
            await page.SaveAndPrintCommand.ExecuteAsync(null);
            Assert.True(string.IsNullOrEmpty(page.ErrorMessage), page.ErrorMessage);
            Sale sale = await shop.Database.Context.Sales.AsNoTracking().SingleAsync();
            Assert.Equal("Mr X", (await shop.Database.Context.Patients.AsNoTracking().SingleAsync(p => p.Id == sale.PatientId)).Name);
            Assert.Equal(before - 2m, await shop.OnHandAsync(shop.Catalog.ParacetamolBatch.Id));
            Assert.Equal(5m, await shop.OnHandAsync(later.Id));
            host.Close();
        });
    }

    [Fact]
    public void NotInStock_OffersBothWaysIn_AndTheSaleContinuesOnceStocked()
    {
        Ui.Run(async () =>
        {
            await using Shop shop = await Shop.OpenAsync();
            var limcee = new CatalogMedicine { SourceId = "L-1", Name = "Limcee 500 Chewable Tablet", CompositionKey = "vitamin c 500mg", ShortComposition1 = "Vitamin C (500mg)", PackSizeLabel = "15 Tablets / Strip" };
            shop.Database.Context.CatalogMedicines.Add(limcee);
            await shop.Database.Context.SaveChangesAsync();
            await new CatalogImportService(shop.Database.Context).EnsureFtsIndexAsync();
            await shop.Database.Context.Database.ExecuteSqlRawAsync("INSERT INTO CatalogMedicineFts (MedicineId, Name, Composition, Manufacturer) VALUES ({0}, {1}, {2}, '')", limcee.Id.ToString("N"), limcee.Name, "Vitamin C (500mg)");
            Window host = await shop.ShowBillingAsync();
            RetailBillingViewModel page = shop.Page;

            Ui.Type(Ui.Named<TextBox>(host, "Sale command"), "1 strip limcee to ali");
            await PressEnterAsync(page);
            Assert.False(page.HasPosPreview);
            Assert.True(page.HasPosMissing);
            Assert.Equal("\"limcee\" is not in stock", page.PosMissingTitle);
            PosMissingMedicine missing = Assert.Single(page.PosMissing);
            Assert.Equal((limcee.Name, "Drug bank"), (missing.Name, missing.Where));

            // Add via Purchase Entry: Purchases is asked for, with the medicine handed over.
            await Ui.PressAsync(Ui.Single<Button>(host, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Add via Purchase Entry"));
            Assert.Equal(["Purchases"], shop.Navigation.Opened);
            Assert.Equal(limcee.Name, shop.PurchaseEntry.MedicineName);

            // Add directly to stock: the stock form (here, filled in by the test) saves a real batch; the sale continues.
            await Ui.PressAsync(Ui.Single<Button>(host, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Add directly to stock"));
            await Ui.UntilAsync(() => page.HasPosPreview, "the command to be checked again once stocked");
            Assert.Equal(limcee.Name, shop.AddStock.Requested!.MedicineName);
            Assert.Equal(1m, page.PosPreview!.PackQuantity);
            Assert.Equal("Ali", page.PosPreview.Customer);
            await Ui.PressAsync(Ui.Single<Button>(host, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Add command to bill"));
            Assert.Equal(limcee.Name, Assert.Single(page.BillItems).Choice.DrugName);
            host.Close();

            // Purchases opens on that medicine.
            var purchases = new PurchasePageViewModel(shop.Services.GetRequiredService<IServiceScopeFactory>(), shop.Session, null!, null!, null!, null!, null!, null!, null!, null!, null!, shop.PurchaseEntry);
            shop.PurchaseEntry.MedicineName = "Limcee 500 Chewable Tablet";
            await purchases.LoadAsync();
            Assert.True(purchases.IsAddItemOpen);
            Assert.Equal("Limcee 500 Chewable Tablet", purchases.MedicineSearchText);
            Assert.Null(shop.PurchaseEntry.MedicineName);
        });
    }

    [Fact]
    public void Tablets_AreConvertedToPacks_AndUnclearCommandsSayWhy()
    {
        Ui.Run(async () =>
        {
            await using Shop shop = await Shop.OpenAsync();
            await shop.Database.Context.Database.ExecuteSqlRawAsync("UPDATE Drugs SET Unit = '10 Tablets / Strip' WHERE Id = {0}", shop.Catalog.Paracetamol650.Id);
            Window host = await shop.ShowBillingAsync();
            RetailBillingViewModel page = shop.Page;
            TextBox box = Ui.Named<TextBox>(host, "Sale command");

            Ui.Type(box, "5 tablets paracetamol 650");
            await PressEnterAsync(page);
            Assert.Equal(0.5m, page.PosPreview!.PackQuantity);

            Ui.Type(box, "sold 2 strips");
            await PressEnterAsync(page);
            Assert.False(page.HasPosPreview);
            Assert.Equal("Which medicine? For example: 1 strip paracetamol to John.", page.PosMessage);

            Ui.Type(box, "900 strips paracetamol 650");
            await PressEnterAsync(page);
            Assert.False(page.PosPreview!.EnoughStock);
            await PressEnterAsync(page);
            Assert.Empty(page.BillItems);
            Assert.StartsWith("Only ", page.PosMessage);
            host.Close();
        });
    }

    private static async Task PressEnterAsync(RetailBillingViewModel page)
    {
        await page.SubmitPosCommandCommand.ExecuteAsync(null);
        await Ui.SettleAsync();
    }

    private sealed class Shop : IAsyncDisposable
    {
        private Shop(DatabaseTestContext database, E2EHealthSeed.Catalog catalog) => (Database, Catalog) = (database, catalog);

        public DatabaseTestContext Database { get; }
        public E2EHealthSeed.Catalog Catalog { get; }
        public ServiceProvider Services { get; private set; } = null!;
        public CurrentSession Session { get; } = new();
        public RecordingNavigation Navigation { get; } = new();
        public PurchaseEntryHandoff PurchaseEntry { get; } = new();
        public StockingDialog AddStock { get; private set; } = null!;
        public RetailBillingViewModel Page { get; private set; } = null!;

        public static async Task<Shop> OpenAsync()
        {
            var database = await DatabaseTestContext.CreateAsync();
            var shop = new Shop(database, await E2EHealthSeed.SeedAsync(database.Context));
            var allowed = new Allowed();
            // Each scope gets its own context, as in the app: the billing screen looks up loyalty points and patients
            // on background threads, and a context shared with the save is not thread-safe.
            shop.Services = new ServiceCollection()
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
                .AddScoped<RetailBillingService>()
                .AddScoped<PosMedicineMatcher>()
                .AddScoped<PurchaseService>()
                .AddScoped<AddStockService>()
                .BuildServiceProvider();
            shop.Session.SignIn(shop.Catalog.Admin);
            shop.AddStock = new StockingDialog(shop);
            IServiceScopeFactory scopes = shop.Services.GetRequiredService<IServiceScopeFactory>();
            shop.Page = new RetailBillingViewModel(scopes, shop.Session, null!, new Quiet(), new CatalogSearchViewModel(scopes, NullLogger<CatalogSearchViewModel>.Instance),
                new NoPrint(), null!, shop.Navigation, null!, new DocumentOutputSettingsStore(Path.Combine(database.Storage.RootDirectory, "print")), null!, null!, null!,
                new QuotationConversionHandoff(), new RetailReissueHandoff(), shop.AddStock, shop.PurchaseEntry);
            return shop;
        }

        public async Task<Window> ShowBillingAsync()
        {
            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = Page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(RetailBillingViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            return host;
        }

        public async Task<Batch> AddBatchAsync(Drug drug, string batchNo, DateOnly expiry, decimal quantity)
        {
            UnitOfWork uow = Database.CreateUnitOfWork();
            var addStock = new AddStockService(uow, Database.CreatePurchaseService(new Allowed(), uow), new Allowed(), new NullLicenseRuntimeGuard());
            Supplier supplier = await Database.Context.Suppliers.FirstAsync();
            AddStockResult result = await addStock.AddStockAsync(new AddStockInput(drug.Id, null, drug.Name, drug.GenericName, null, drug.Schedule ?? "G", batchNo, expiry,
                30m, 18m, quantity, 0m, 12m, 5m, supplier.Id, "INV-" + batchNo, BusinessClock.Today, "A1", HsnCode: "3004"), Catalog.Admin.Id, UserRole.Owner);
            return await Database.Context.Batches.AsNoTracking().SingleAsync(batch => batch.Id == result.BatchId);
        }

        public Task<decimal> OnHandAsync(Guid batchId) =>
            Database.Context.StockMovements.AsNoTracking().Where(m => m.BatchId == batchId).SumAsync(m => m.QuantityChange);

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    // Stands in for the Add Stock form: the pharmacist enters a real batch, expiry, MRP and quantity, and saves.
    private sealed class StockingDialog(Shop shop) : IAddStockDialogService
    {
        public AddStockRequest? Requested { get; private set; }

        public async Task<bool> ShowAsync(AddStockRequest request)
        {
            Requested = request;
            UnitOfWork uow = shop.Database.CreateUnitOfWork();
            var addStock = new AddStockService(uow, shop.Database.CreatePurchaseService(new Allowed(), uow), new Allowed(), new NullLicenseRuntimeGuard());
            Supplier supplier = await shop.Database.Context.Suppliers.FirstAsync();
            await addStock.AddStockAsync(new AddStockInput(request.DrugId, request.CatalogMedicineId, request.MedicineName, "Vitamin C", null, "G", "LIM-01",
                BusinessClock.Today.AddYears(1), 45m, 30m, 10m, 0m, 12m, 5m, supplier.Id, "INV-LIM", BusinessClock.Today, "B2", HsnCode: "30045090"), shop.Catalog.Admin.Id, UserRole.Owner);
            return true;
        }
    }

    public sealed class RecordingNavigation : INavigationService
    {
        public List<string> Opened { get; } = [];
        public string CurrentSectionKey => Opened.LastOrDefault() ?? "Billing";
        public event EventHandler? SectionChanged;
        public void Navigate(string sectionKey)
        {
            Opened.Add(sectionKey);
            SectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Quiet : IConfirmationService
    {
        public bool Confirm(string message, string title) => true;
        public bool? Choose(string message, string title, string acceptLabel, string alternateLabel) => true;
        public void Notify(string title, string message) { }
        public void NotifyError(string title, string message) { }
    }

    private sealed class NoPrint : IInvoicePrintService
    {
        public Task<string> PrintAsync(Guid saleId, InvoiceTemplateType? templateOverride = null, bool? silentOverride = null, CancellationToken cancellationToken = default) => Task.FromResult("printed");
        public Task<string> PreviewAsync(Guid saleId, InvoiceTemplateType template = InvoiceTemplateType.StandardA4, CancellationToken cancellationToken = default) => Task.FromResult("preview");
        public Task<string?> PickFormatAndPrintAsync(Guid saleId, CancellationToken cancellationToken = default) => Task.FromResult<string?>("printed");
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
