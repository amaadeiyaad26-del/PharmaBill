using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Pack levels, batch rates and wholesale pricing, used the way a shopkeeper would: choose the medicine, then its batch;
// every field labelled; what is saved now shown; refusals that say why; confirmations that say what was saved.
public sealed class WholesalePricingScreenTests
{
    [Theory]
    [InlineData(-1.0, null, null, 20.0, "Purchase rate cannot be negative.")]
    [InlineData(10.0, -1.0, null, 20.0, "PTR (price to retailer) cannot be negative.")]
    [InlineData(10.0, null, -1.0, 20.0, "PTS (price to stockist) cannot be negative.")]
    [InlineData(10.0, 25.0, null, 20.0, "PTR (price to retailer) ₹25.00 cannot be more than the MRP ₹20.00.")]
    [InlineData(10.0, null, 21.0, 20.0, "PTS (price to stockist) ₹21.00 cannot be more than the MRP ₹20.00.")]
    [InlineData(10.0, 16.0, 18.0, 0.0, "Enter the MRP printed on the pack; it must be more than ₹0.")]
    [InlineData(10.0, 16.0, 18.0, 20.0, null)]
    public void BatchRates_AreRefusedWithTheReason(double purchase, double? ptr, double? pts, double mrp, string? expected) =>
        Assert.Equal(expected, WholesalePricingService.BatchRateProblem(new WholesaleBatchRate((decimal)purchase, (decimal?)ptr, (decimal?)pts, (decimal)mrp)));

    [Fact]
    public async Task UnitsPerPack_MustBeAWholeNumber()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "Pack medicine" };
        database.Context.Drugs.Add(drug);
        await database.Context.SaveChangesAsync();
        var service = new WholesalePricingService(new UnitOfWork(database.Context));
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SavePackLevelsAsync(drug.Id,
        [
            new DrugPackLevel { Level = PackLevel.Unit, Label = "tablet", UnitsPerPack = 1m },
            new DrugPackLevel { Level = PackLevel.Strip, Label = "strip", UnitsPerPack = 2.5m }
        ]));
        Assert.Contains("whole number above zero", refused.Message);
    }

    private sealed record Shop(AppUser User, Drug Amoxyclav, Batch Early, Batch Late, Customer Hospital);

    private static async Task<Shop> SeedAsync(PharmaBillDbContext context)
    {
        var user = new AppUser { UserName = "owner1", DisplayName = "Owner", Role = UserRole.Owner, PasswordHash = "x", IsActive = true };
        var amoxyclav = new Drug { Name = "Amoxyclav 625 Tablet", IsActive = true, Mrp = 20m };
        var other = new Drug { Name = "Dolo 650 Tablet", IsActive = true };
        var early = new Batch { DrugId = amoxyclav.Id, BatchNo = "AMX-01", ExpiryDate = new DateOnly(2027, 8, 31), PurchasePrice = 10m, Ptr = 16m, Pts = 14m, Mrp = 20m };
        var late = new Batch { DrugId = amoxyclav.Id, BatchNo = "AMX-02", ExpiryDate = new DateOnly(2028, 1, 31), PurchasePrice = 11m, Mrp = 20m };
        var hospital = new Customer { Name = "Valley Hospital Pharmacy", Phone = "9876511111", PriceCategory = "Hospital", IsActive = true };
        var invoice = new WholesaleInvoice { CustomerId = hospital.Id, InvoiceNo = "WS-0042", InvoiceAtUtc = new DateTime(2026, 10, 12, 6, 30, 0, DateTimeKind.Utc), Status = "Posted" };
        context.AddRange(user, amoxyclav, other, early, late, hospital, invoice,
            new StockMovement { BatchId = early.Id, DrugId = amoxyclav.Id, QuantityChange = 50m, MovementType = "Purchase" },
            new StockMovement { BatchId = early.Id, DrugId = amoxyclav.Id, QuantityChange = -10m, MovementType = "Sale" },
            new WholesaleRateHistory { CustomerId = hospital.Id, DrugId = amoxyclav.Id, BatchId = early.Id, InvoiceId = invoice.Id, SoldAtUtc = invoice.InvoiceAtUtc, UnitRate = 15.5m, Quantity = 12m });
        await context.SaveChangesAsync();
        return new Shop(user, amoxyclav, early, late, hospital);
    }

    private sealed class Margins(bool canView) : IUserSessionService
    {
        public AppUser? CurrentUser => null;
        public bool IsAuthenticated => true;
        public UserRole? CurrentRole => UserRole.Owner;
        public string OperatorRoleLabel => "Admin";
        public bool CanViewPurchaseMargins => canView;
        public bool CanEditPrintedBills => true;
        public bool CanAccessSettings => true;
        public bool CanManageUsers => true;
        public bool CanCreateBill => true;
        public bool Allows(PharmaBill.Core.Security.AppPermission permission) => true;
    }

    [Fact]
    public void PricingScreen_ThroughTheScreen()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using var database = await DatabaseTestContext.CreateAsync();
            Shop shop = await SeedAsync(database.Context);
            ServiceProvider services = new ServiceCollection()
                .AddScoped(_ => database.OpenContext())
                .AddScoped(sp => new WholesalePricingService(new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>())))
                .BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(shop.User);
            var page = new WholesalePricingPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, new Margins(canView: true));
            await page.LoadAsync();

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1366, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(WholesalePricingPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();

            // Nothing chosen: it says what to do first, and nothing below can be used.
            Assert.Equal("Choose a medicine and batch first.", Ui.Named<TextBlock>(host, "Selection hint").Text);
            Assert.False(Ui.Named<StackPanel>(host, "Pricing sections").IsEnabled);
            Assert.False(Ui.Named<ComboBox>(host, "Batch").IsEnabled);
            foreach (string label in new[] { "Medicine", "Batch", "Purchase rate (₹ per pack)", "PTR · price to retailer (₹)", "PTS · price to stockist (₹)", "MRP (₹, as printed)", "Base units per pack", "Discount (%)", "Customer" })
            {
                Assert.Contains(label, Texts(host));
            }
            Assert.Contains("Price to retailer", (string)Ui.Named<TextBox>(host, "PTR").ToolTip);

            // Type-ahead by name, then the medicine's own batches with expiry and stock.
            page.MedicineSearch = "amoxy";
            Assert.Equal(["Amoxyclav 625 Tablet"], page.MedicineMatches.Select(drug => drug.Name));
            Ui.Named<ComboBox>(host, "Medicine").SelectedItem = page.MedicineMatches[0];
            await page.MedicineLoading;
            await Ui.SettleAsync();
            Assert.Equal(["AMX-01  ·  Exp 08/2027  ·  40 in stock", "AMX-02  ·  Exp 01/2028  ·  0 in stock"], page.Batches.Select(batch => batch.Label));
            Assert.Equal("Now choose the batch.", page.SelectionHint);
            Assert.False(Ui.Named<StackPanel>(host, "Pricing sections").IsEnabled);
            Ui.Named<ComboBox>(host, "Batch").SelectedItem = page.Batches[0];
            await Ui.SettleAsync();
            Assert.True(Ui.Named<StackPanel>(host, "Pricing sections").IsEnabled);

            // The saved values are shown and filled in, not empty boxes.
            Assert.Equal("Saved now: Purchase ₹10.00  ·  PTR ₹16.00  ·  PTS ₹14.00  ·  MRP ₹20.00", Ui.Named<TextBlock>(host, "Saved batch rates").Text);
            Assert.Equal(("10.00", "16.00", "14.00", "20.00"), (page.PurchaseRateText, page.PtrText, page.PtsText, page.MrpText));

            // Refused with the reason; then saved, and it says what.
            Ui.Type(Ui.Named<TextBox>(host, "PTR"), "25");
            await Ui.PressAsync(Ui.Button(host, "Save batch rates"));
            Assert.Equal("PTR (price to retailer) ₹25.00 cannot be more than the MRP ₹20.00.", Ui.Named<TextBlock>(host, "Batch rates problem").Text);
            Ui.Type(Ui.Named<TextBox>(host, "PTS"), "-2");
            Ui.Type(Ui.Named<TextBox>(host, "PTR"), "17");
            await Ui.PressAsync(Ui.Button(host, "Save batch rates"));
            Assert.Equal("PTS (price to stockist) cannot be negative.", page.RatesError);
            Ui.Type(Ui.Named<TextBox>(host, "PTS"), "15");
            await Ui.PressAsync(Ui.Button(host, "Save batch rates"));
            Assert.Equal("Saved for batch AMX-01: PTR ₹17.00, PTS ₹15.00, MRP ₹20.00.", Ui.Named<TextBlock>(host, "Batch rates saved").Text);
            Assert.Equal(string.Empty, page.RatesError);
            await using (PharmaBillDbContext check = database.OpenContext())
            {
                Batch saved = await check.Batches.AsNoTracking().SingleAsync(batch => batch.Id == shop.Early.Id);
                Assert.Equal((17m, 15m), (saved.Ptr, saved.Pts));
            }
            Assert.Contains("PTR ₹17.00", page.SavedRatesText);

            // Pack sizes: a whole number above zero.
            Assert.Contains("No pack sizes saved for this medicine yet; its stock counts in single units.", Texts(host));
            Ui.Type(Ui.Named<TextBox>(host, "Pack name"), "Strip of 10");
            Ui.Type(Ui.Named<TextBox>(host, "Units per pack"), "2.5");
            await Ui.PressAsync(Ui.Button(host, "Save pack size"));
            Assert.Equal("Units per pack must be a whole number above zero, for example 10.", page.PackError);
            Ui.Type(Ui.Named<TextBox>(host, "Units per pack"), "10");
            await Ui.PressAsync(Ui.Button(host, "Save pack size"));
            Assert.Equal("Saved: 1 Strip of 10 = 10 base units.", page.PackMessage);
            Assert.Equal([(PackLevel.Unit, 1m), (PackLevel.Strip, 10m)], page.PackLevels.Select(level => (level.Level, level.UnitsPerPack)));

            // Customer pricing: each part saves with its own fields and says what it saved; the saved list shows it.
            Assert.Contains("No customer prices, discounts or schemes saved for this medicine yet.", Texts(host));
            Ui.Type(Ui.Named<TextBox>(host, "Category price"), "15");
            await Ui.PressAsync(Ui.Button(host, "Save category price"));
            Assert.Equal("Choose or type the customer category, for example Hospital.", page.PriceError);
            page.PriceCategory = "Hospital";
            await Ui.PressAsync(Ui.Button(host, "Save category price"));
            Assert.StartsWith("Saved: Hospital pays ₹15.00 per pack for Amoxyclav 625 Tablet", page.PriceMessage);
            page.DiscountCategory = "Hospital";
            Ui.Type(Ui.Named<TextBox>(host, "Discount percent"), "120");
            await Ui.PressAsync(Ui.Button(host, "Save trade discount"));
            Assert.Equal("A discount cannot be more than 100%.", page.DiscountError);
            Ui.Type(Ui.Named<TextBox>(host, "Discount percent"), "5");
            await Ui.PressAsync(Ui.Button(host, "Save trade discount"));
            Assert.StartsWith("Saved: 5% trade discount for Hospital", page.DiscountMessage);
            Ui.Type(Ui.Named<TextBox>(host, "Scheme buy"), "10");
            Ui.Type(Ui.Named<TextBox>(host, "Scheme free"), "1");
            page.SchemeTo = new DateTime(2026, 12, 31);
            await Ui.PressAsync(Ui.Button(host, "Save scheme"));
            Assert.StartsWith("Saved: buy 10, get 1 free on Amoxyclav 625 Tablet for all customers", page.SchemeMessage);
            Assert.Equal(["Category price", "Trade discount", "Scheme"], page.SavedPricing.Select(row => row.Kind));

            // Recent rates: a customer found by phone, dates in IST, no column names on screen.
            Assert.Equal("Choose a customer to see the last five rates they were billed for this medicine.", Ui.Named<TextBlock>(host, "Recent rates empty").Text);
            page.CustomerSearch = "98765";
            Assert.Equal(["Valley Hospital Pharmacy  ·  9876511111"], page.CustomerMatches.Select(choice => choice.Label));
            Ui.Named<ComboBox>(host, "Customer").SelectedItem = page.CustomerMatches[0];
            await page.RatesLoading;
            await Ui.SettleAsync();
            RecentRateRow rate = Assert.Single(page.RecentRates);
            Assert.Equal(("12-Oct-2026  12:00 PM", "WS-0042", "15.50", "12"), (rate.SoldOn, rate.InvoiceNo, rate.RateText, rate.QuantityText));
            DataGrid recent = Ui.Named<DataGrid>(host, "Recent rates");
            Assert.Equal(["Sold on (IST)", "Invoice no.", "Rate (₹ per pack)", "Quantity"], recent.Columns.Select(column => (string)column.Header));
            Assert.DoesNotContain(Texts(host), text => text.Contains("Utc", StringComparison.Ordinal) || text.Contains("00000000-", StringComparison.Ordinal));
            Assert.Equal(string.Empty, page.ErrorMessage);
            bindings.AssertNone();
            host.Close();
        });
    }

    [Fact]
    public async Task WithoutMarginPermission_ThePurchaseRateIsKeptAsSaved()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SeedAsync(database.Context);
        ServiceProvider services = new ServiceCollection()
            .AddScoped(_ => database.OpenContext())
            .AddScoped(sp => new WholesalePricingService(new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>())))
            .BuildServiceProvider();
        var session = new CurrentSession();
        session.SignIn(shop.User);
        var page = new WholesalePricingPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, new Margins(canView: false));
        await page.LoadAsync();
        page.SelectedDrug = page.MedicineMatches.Single(drug => drug.Id == shop.Amoxyclav.Id);
        await page.MedicineLoading;
        page.SelectedBatch = page.Batches[0];
        Assert.DoesNotContain("Purchase", page.SavedRatesText);
        page.PurchaseRateText = "999";
        page.PtrText = "18";
        await page.SaveBatchRatesCommand.ExecuteAsync(null);
        Assert.Equal(string.Empty, page.RatesError);
        await using PharmaBillDbContext check = database.OpenContext();
        Batch saved = await check.Batches.AsNoTracking().SingleAsync(batch => batch.Id == shop.Early.Id);
        Assert.Equal((10m, 18m), (saved.PurchasePrice, saved.Ptr));
    }

    private static List<string> Texts(DependencyObject root)
    {
        var texts = new List<string>();
        void Walk(DependencyObject node)
        {
            if (node is TextBlock { IsVisible: true } block && !string.IsNullOrEmpty(block.Text))
            {
                texts.Add(block.Text);
            }
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            {
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
            }
        }
        Walk(root);
        return texts;
    }
}
