using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaBill.App.Controls;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// The medicine search results float in a flyout under the search box on Retail and Stock (and in Wholesale's dropdown):
// three lines per medicine (name and price; salt; maker and pack), actions on the right, and the page below never moves.
public sealed class MedicineSearchFlyoutTests
{
    [Fact]
    public void Retail_ResultsFloatOverTheBill_ShowSaltMakerAndPack_AndCloseOnAnOutsideClick()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using Pharmacy shop = await Pharmacy.OpenAsync();
            RetailBillingViewModel page = shop.RetailPage();
            Window host = await shop.ShowAsync(page, typeof(RetailBillingViewModel));
            DataGrid bill = Ui.Single<DataGrid>(host, grid => grid.Name == "RetailBillGrid");
            Point billBefore = bill.TranslatePoint(new Point(0, 0), host);
            TextBox search = Ui.Single<TextBox>(host, box => box.Name == "RetailItemSearchBox");

            Ui.Type(search, "Paracetamol");
            await Ui.UntilAsync(() => page.HasSearchResults, "the stock search");
            Popup flyout = Find<Popup>(host, "RetailSearchFlyout");
            Assert.True(flyout.IsOpen);
            Border card = AssertCard(flyout);
            List<string> lines = Texts(card);
            Assert.Contains("Paracetamol (650mg)", lines);
            Assert.Contains("Acme Pharma • 15 Tablets / Strip", lines);
            Assert.Contains(lines, line => line.StartsWith("Batch " + shop.Catalog.ParacetamolBatch.BatchNo + " · Exp "));
            Assert.Contains(lines, line => line.StartsWith('₹'));
            // Floating: the bill has not been pushed down.
            Assert.Equal(billBefore, bill.TranslatePoint(new Point(0, 0), host));

            // A click on the bill closes it, and the bill still gets that click.
            bool billClicked = false;
            bill.PreviewMouseDown += (_, _) => billClicked = true;
            Click(bill);
            Assert.False(flyout.IsOpen);
            Assert.False(page.IsSearchFlyoutOpen);
            Assert.True(billClicked, "the outside click reached the bill");

            // Back in the search box, the results come back; Esc closes them.
            search.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent });
            Assert.True(flyout.IsOpen);
            search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            Assert.False(flyout.IsOpen);

            // The row's own Add button bills that batch and the flyout goes away.
            page.ReopenSearchFlyoutCommand.Execute(null);
            await Ui.SettleAsync();
            Button add = Ui.Button(AssertCard(flyout), "Add");
            AssertOnTheRight(add, card);
            await Ui.PressAsync(add);
            Assert.Equal(shop.Catalog.ParacetamolBatch.Id, Assert.Single(page.BillItems).Choice.BatchId);
            Assert.False(flyout.IsOpen);
            bindings.AssertNone();
            host.Close();
        });
    }

    [Fact]
    public void Retail_DrugBankOnlyRow_OffersAddToStockAndSubstitutesOnTheRight()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using Pharmacy shop = await Pharmacy.OpenAsync();
            RetailBillingViewModel page = shop.RetailPage();
            Window host = await shop.ShowAsync(page, typeof(RetailBillingViewModel));

            Ui.Type(Ui.Single<TextBox>(host, box => box.Name == "RetailItemSearchBox"), "Limcee");
            await Ui.UntilAsync(() => page.HasCatalogOnlyMatches, "the drug bank match");
            Border card = AssertCard(Find<Popup>(host, "RetailSearchFlyout"));
            List<string> lines = Texts(card);
            Assert.Contains("Limcee 500 Chewable Tablet", lines);
            Assert.Contains("Vitamin C (500mg)", lines);
            Assert.Contains("Abbott • 15 Tablets / Strip", lines);
            Assert.Contains("No stock", lines);
            AssertOnTheRight(Ui.Button(card, "Substitutes"), card);
            Button addToStock = Ui.Button(card, "Add to stock");
            AssertOnTheRight(addToStock, card);
            await Ui.PressAsync(addToStock);
            Assert.Equal("Limcee 500 Chewable Tablet", shop.AddStock.Requested?.MedicineName);
            bindings.AssertNone();
            host.Close();
        });
    }

    [Fact]
    public void Stock_SearchControl_FloatsItsResults_AndOnlyOpensForItsOwnSearch()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using Pharmacy shop = await Pharmacy.OpenAsync();
            // The main window shows the search on the Stock section.
            var search = new CatalogSearchViewModel(shop.Scopes, NullLogger<CatalogSearchViewModel>.Instance) { IsVisible = true };
            var control = new GlobalMedicineSearchControl { ShowActions = true, DataContext = search };
            var elsewhere = new TextBox();
            var below = new Border { Height = 40, Background = Brushes.LightGray };
            var panel = new StackPanel();
            panel.Children.Add(elsewhere);
            panel.Children.Add(control);
            panel.Children.Add(below);
            Window host = await Ui.ShowAsync(panel);
            Point belowBefore = below.TranslatePoint(new Point(0, 0), host);

            TextBox box = Ui.Single<TextBox>(control, candidate => candidate.IsVisible);
            Ui.Type(box, "Paracetamol");
            await Ui.UntilAsync(() => search.Query == "Paracetamol", "the search text");
            await Ui.PressAsync(Ui.Button(control, "Search"));
            await Ui.UntilAsync(() => search.HasInStock, "the stock search");
            Assert.True(control.IsResultsFlyoutOpen);
            Border card = AssertCard(Find<Popup>(control, "ResultsFlyout"));
            List<string> lines = Texts(card);
            Assert.Contains("Paracetamol (650mg)", lines);
            Assert.Contains("Acme Pharma • 15 Tablets / Strip", lines);
            Assert.Contains(lines, line => line.StartsWith('₹'));
            // Every row carries its actions on the right.
            foreach (string action in new[] { "Add stock (new batch)", "Adjust", "Batches", "Substitutes" })
            {
                List<Button> buttons = Descendants(card).OfType<Button>().Where(b => b.IsVisible && Equals(b.Content, action)).ToList();
                Assert.NotEmpty(buttons);
                buttons.ForEach(button => AssertOnTheRight(button, card));
            }
            // Nothing below is pushed down (it may move up: the "type 2 or more letters" hint goes once a search runs).
            Assert.True(below.TranslatePoint(new Point(0, 0), host).Y <= belowBefore.Y, "the results pushed the page down");

            Click(below);
            Assert.False(control.IsResultsFlyoutOpen);

            // The header search shares this view model: a search made elsewhere does not pop this flyout open.
            elsewhere.Focus();
            await search.SearchCommand.ExecuteAsync(null);
            await Ui.SettleAsync();
            Assert.False(control.IsResultsFlyoutOpen);
            bindings.AssertNone();
            host.Close();
        });
    }

    [Fact]
    public void Wholesale_Dropdown_UsesTheSameThreeLineRow()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using Pharmacy shop = await Pharmacy.OpenAsync();
            var page = new WholesaleBillingPageViewModel(shop.Scopes, shop.Session, null!, null!, null!, null!, null!, null!, null!, null!,
                new WholesaleReissueHandoff(), new QuotationConversionHandoff(), null!, null!);
            Window host = await shop.ShowAsync(page, typeof(WholesaleBillingPageViewModel));
            page.MedicinePickerItems.Add(WholesaleMedicinePickerItem.FromCatalogue(Guid.NewGuid(), "Limcee 500 Chewable Tablet", "Vitamin C (500mg)", "Abbott", 22.5m, null));
            ComboBox picker = Ui.Single<ComboBox>(host, combo => combo.Name == "WholesaleMedicineSearchBox");
            Assert.Equal(380, picker.MaxDropDownHeight);
            picker.IsDropDownOpen = true;
            await Ui.SettleAsync();
            var row = (FrameworkElement)picker.ItemContainerGenerator.ContainerFromIndex(0);
            List<string> lines = Texts(row);
            Assert.Contains("Limcee 500 Chewable Tablet", lines);
            Assert.Contains("Vitamin C (500mg)", lines);
            Assert.Contains("Abbott", lines);
            Assert.Contains("MRP ₹22.50", lines);
            picker.IsDropDownOpen = false;
            bindings.AssertNone();
            host.Close();
        });
    }

    // ---- helpers ----

    private static Border AssertCard(Popup flyout)
    {
        var card = Assert.IsType<Border>(flyout.Child);
        Assert.Equal(380, card.MaxHeight);
        Assert.Equal(new CornerRadius(8), card.CornerRadius);
        Assert.NotNull(card.Effect);
        Assert.Equal(ScrollBarVisibility.Auto, Assert.IsType<ScrollViewer>(card.Child).VerticalScrollBarVisibility);
        return card;
    }

    private static void AssertOnTheRight(FrameworkElement button, FrameworkElement card) =>
        Assert.True(button.TranslatePoint(new Point(0, 0), card).X > card.ActualWidth / 2, "the action sits on the right of the row");

    private static void Click(UIElement target) =>
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });

    private static List<string> Texts(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text).ToList();

    private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants(root).OfType<T>().Single(element => element.Name == name);

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

    // A shop with Paracetamol in stock (linked to its drug bank row) and Limcee only in the drug bank.
    private sealed class Pharmacy : IAsyncDisposable
    {
        private ServiceProvider _services = null!;

        private Pharmacy(DatabaseTestContext database, E2EHealthSeed.Catalog catalog) => (Database, Catalog) = (database, catalog);

        public DatabaseTestContext Database { get; }
        public E2EHealthSeed.Catalog Catalog { get; }
        public CurrentSession Session { get; } = new();
        public RecordingAddStock AddStock { get; } = new();
        public IServiceScopeFactory Scopes => _services.GetRequiredService<IServiceScopeFactory>();

        public static async Task<Pharmacy> OpenAsync()
        {
            var database = await DatabaseTestContext.CreateAsync();
            var shop = new Pharmacy(database, await E2EHealthSeed.SeedAsync(database.Context));
            var paracetamol = new CatalogMedicine { SourceId = "P-650", Name = "Paracetamol 650mg", CompositionKey = "paracetamol 650mg", ShortComposition1 = "Paracetamol (650mg)", Manufacturer = "Acme Pharma", PackSizeLabel = "15 Tablets / Strip", ReferencePrice = 30m };
            var limcee = new CatalogMedicine { SourceId = "L-1", Name = "Limcee 500 Chewable Tablet", CompositionKey = "vitamin c 500mg", ShortComposition1 = "Vitamin C (500mg)", Manufacturer = "Abbott", PackSizeLabel = "15 Tablets / Strip", ReferencePrice = 22.5m };
            database.Context.CatalogMedicines.AddRange(paracetamol, limcee);
            await database.Context.SaveChangesAsync();
            await database.Context.Database.ExecuteSqlRawAsync("UPDATE Drugs SET CatalogMedicineId = {0} WHERE Id = {1}", paracetamol.Id, shop.Catalog.Paracetamol650.Id);
            await new CatalogImportService(database.Context).EnsureFtsIndexAsync();
            foreach (CatalogMedicine medicine in new[] { paracetamol, limcee })
            {
                await database.Context.Database.ExecuteSqlRawAsync("INSERT INTO CatalogMedicineFts (MedicineId, Name, Composition, Manufacturer) VALUES ({0}, {1}, {2}, {3})",
                    medicine.Id.ToString("N"), medicine.Name, medicine.ShortComposition1!, medicine.Manufacturer!);
            }
            var allowed = new Allowed();
            // Each scope gets its own context, as in the app: the screens search on background threads.
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
                .AddScoped<RetailBillingService>()
                .BuildServiceProvider();
            shop.Session.SignIn(shop.Catalog.Admin);
            return shop;
        }

        public RetailBillingViewModel RetailPage() =>
            new(Scopes, Session, null!, new Quiet(), new CatalogSearchViewModel(Scopes, NullLogger<CatalogSearchViewModel>.Instance),
                null!, null!, null!, null!, new DocumentOutputSettingsStore(Path.Combine(Database.Storage.RootDirectory, "print")), null!, null!, null!,
                new QuotationConversionHandoff(), new RetailReissueHandoff(), AddStock);

        public async Task<Window> ShowAsync(object page, Type pageType)
        {
            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(pageType)], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            await Database.DisposeAsync();
        }
    }

    private sealed class RecordingAddStock : IAddStockDialogService
    {
        public AddStockRequest? Requested { get; private set; }

        public Task<bool> ShowAsync(AddStockRequest request)
        {
            Requested = request;
            return Task.FromResult(false);
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
