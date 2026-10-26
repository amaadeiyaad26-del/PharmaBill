using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

// Stock Management and Accounts & Parties: one sidebar item each, whose tabs show the existing screens with their
// own data. A patient, buyer or supplier in the directory opens their ledger in the Ledgers tab.
public sealed class HubScreensTests
{
    [Fact]
    public void StockManagement_TabsShowInventoryStockInHandAndTransfer()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using Shop shop = await Shop.OpenAsync();
            var hub = new HubPageViewModel("StockHub", "Stock Management", "Inventory and batches, stock in hand and transfers.");
            var stock = new StockPageViewModel(shop.Scopes, shop.Session, new UserSessionService(shop.Session), new Quiet(), null!, null!, new TabularExportService(), null!, null!);
            var inHand = new StockInHandPageViewModel(shop.Scopes, new UserSessionService(shop.Session));
            var transfer = new StockTransferPageViewModel(shop.Scopes, shop.Session, new Quiet(), null!, new TabularExportService(), null!);
            hub.SetTabs(
            [
                new HubTab("inventory", "Inventory Master & Batches", stock),
                new HubTab("inhand", "Stock in Hand", inHand),
                new HubTab("transfer", "Stock Transfer", transfer),
            ]);
            Window host = await shop.ShowAsync(hub);
            await hub.LoadAsync();
            await Ui.SettleAsync();

            // One title over the tabs; the inventory page's own "Stock" heading hides under it.
            Assert.Single(Texts(host), text => text == "Stock Management");
            Assert.DoesNotContain(Descendants(host).OfType<TextBlock>(), text => text.IsVisible && text.Text == "Stock" && text.FontSize >= 26);
            Assert.True(Equals(true, Ui.Button(host, "Inventory Master & Batches").Tag));
            Assert.Contains(stock.Rows, row => row.Medicine == "Paracetamol 650mg");
            Assert.True(string.IsNullOrEmpty(hub.ErrorMessage), hub.ErrorMessage);

            await Ui.PressAsync(Ui.Button(host, "Stock in Hand"));
            Assert.Same(inHand, hub.ActivePage);
            Assert.DoesNotContain("Stock in hand (movement-derived)", Texts(host));
            Assert.True(string.IsNullOrEmpty(inHand.ErrorMessage), inHand.ErrorMessage);

            await Ui.PressAsync(Ui.Button(host, "Stock Transfer"));
            Assert.Same(transfer, hub.ActivePage);
            Assert.DoesNotContain("Stock transfer challan", Texts(host));

            // Back to the inventory: the same page, its rows still there.
            await Ui.PressAsync(Ui.Button(host, "Inventory Master & Batches"));
            Assert.Same(stock, hub.ActivePage);
            Assert.Contains(stock.Rows, row => row.Medicine == "Paracetamol 650mg");
            bindings.AssertNone();
            host.Close();
        });
    }

    [Fact]
    public void AccountsAndParties_DirectoryEntriesOpenTheirLedger()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using Shop shop = await Shop.OpenAsync();
            var patient = new Customer { Name = "Rafiq Ahmad", Phone = "9876500011", BuyerType = CustomerListRules.PatientTypes.First() };
            shop.Database.Context.Customers.Add(patient);
            await shop.Database.Context.SaveChangesAsync();
            Supplier supplier = await shop.Database.Context.Suppliers.AsNoTracking().OrderBy(item => item.Name).FirstAsync();

            var desk = new BillingDeskState { Current = ActiveBillingDeskMode.Retail };
            var accounts = new WholesaleAccountsPageViewModel(shop.Scopes, shop.Session, null!, new TabularExportService(), desk);
            var customers = new WholesaleCustomerPageViewModel(shop.Scopes, null!, null!, shop.Session, new Quiet());
            var suppliers = new SupplierDirectoryPageViewModel(shop.Scopes);
            var hub = new HubPageViewModel("Parties", "Accounts & Parties", "Ledgers and the directory of everyone you buy from and sell to.");
            hub.SetTabs(
            [
                new HubTab(PartyLedgerLinks.LedgersTab, "Accounts & Ledgers", accounts),
                new HubTab("Customers", "Patients & Consumers", customers, onShow: () => customers.SetCustomerView(CustomerListView.Patients)),
                new HubTab("suppliers", "Suppliers", suppliers),
            ]);
            PartyLedgerLinks.Connect(hub, accounts, customers, suppliers);
            Window host = await shop.ShowAsync(hub);
            await hub.LoadAsync();

            // The patient, in the directory: "Open ledger" shows their account in the Ledgers tab.
            await Ui.PressAsync(Ui.Button(host, "Patients & Consumers"));
            Assert.False(Ui.Named<Button>(host, "Open ledger").IsEnabled);
            customers.SelectedRecord = customers.Customers.Single(record => record.Customer.Id == patient.Id);
            await Ui.SettleAsync();
            await Ui.PressAsync(Ui.Named<Button>(host, "Open ledger"));
            await Ui.UntilAsync(() => accounts.SelectedCustomer?.Id == patient.Id, "the patient's ledger");
            Assert.Equal(PartyLedgerLinks.LedgersTab, hub.ActiveTabKey);
            Assert.Equal(1, accounts.SelectedLedgerTab);

            // A supplier opens their payables.
            await Ui.PressAsync(Ui.Button(host, "Suppliers"));
            suppliers.SelectedSupplier = suppliers.Suppliers.Single(item => item.Id == supplier.Id);
            await Ui.SettleAsync();
            await Ui.PressAsync(Ui.Named<Button>(host, "Open ledger"));
            await Ui.UntilAsync(() => accounts.SelectedSupplier?.Id == supplier.Id, "the supplier's payables");
            Assert.Equal(PartyLedgerLinks.LedgersTab, hub.ActiveTabKey);
            Assert.Equal(0, accounts.SelectedLedgerTab);
            bindings.AssertNone();
            host.Close();
        });
    }

    private static List<string> Texts(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text).ToList();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }

    private sealed class Shop : IAsyncDisposable
    {
        private ServiceProvider _services = null!;

        private Shop(DatabaseTestContext database) => Database = database;

        public DatabaseTestContext Database { get; }
        public CurrentSession Session { get; } = new();
        public IServiceScopeFactory Scopes => _services.GetRequiredService<IServiceScopeFactory>();

        public static async Task<Shop> OpenAsync()
        {
            var database = await DatabaseTestContext.CreateAsync();
            var shop = new Shop(database);
            E2EHealthSeed.Catalog catalog = await E2EHealthSeed.SeedAsync(database.Context);
            // Each scope its own context, as in the app: these pages load on background threads.
            shop._services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(database.Storage)
                .AddSingleton(database.BranchSettings)
                .AddSingleton<IEntitlementService, Allowed>()
                .AddSingleton<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>()
                .AddScoped(_ => database.OpenContext())
                .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
                .AddScoped<NumberSeriesService>()
                .AddScoped<StorageLocationService>()
                .AddScoped<BranchService>()
                .AddScoped<CatalogSearchService>()
                .AddScoped<PurchaseService>()
                .AddScoped<InventoryService>()
                .AddScoped<StockIntelligenceService>()
                .AddScoped<ShortageIndentService>()
                .AddScoped<StockVerificationService>()
                .AddScoped<BreakageExpiryReturnService>()
                .AddScoped<StockInHandService>()
                .AddScoped<StockTransferService>()
                .AddScoped<WholesaleAccountsService>()
                .AddScoped<WholesaleCustomerService>()
                .BuildServiceProvider();
            shop.Session.SignIn(catalog.Admin);
            return shop;
        }

        public async Task<Window> ShowAsync(HubPageViewModel hub)
        {
            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = hub, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(HubPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class Quiet : IConfirmationService
    {
        public bool Confirm(string message, string title) => true;
        public bool? Choose(string message, string title, string acceptLabel, string alternateLabel) => true;
        public void Notify(string title, string message) { }
        public void NotifyError(string title, string message) { }
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
