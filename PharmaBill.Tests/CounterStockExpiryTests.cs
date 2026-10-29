using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Counter stock made mid-sale follows the same expiry rule as a purchase: the date from the pack (typed MM/YY) or the
// "No expiry printed on pack" tick, never an invented date. Batches already stored without either are flagged on the
// Stock page, counted, and given a date there.
public sealed class CounterStockExpiryTests
{
    [Theory]
    [InlineData("10/27", 2027, 10)]
    [InlineData("1027", 2027, 10)]
    [InlineData("10-27", 2027, 10)]
    [InlineData("1/28", 2028, 1)]
    [InlineData("10/2027", 2027, 10)]
    [InlineData(" 02/28 ", 2028, 2)]
    public void TypedExpiry_IsTheLastDayOfThatMonth(string typed, int year, int month)
    {
        Assert.True(ExpiryRule.TryParseTyped(typed, out DateOnly expiry));
        Assert.Equal(new DateOnly(year, month, DateTime.DaysInMonth(year, month)), expiry);
    }

    [Theory]
    [InlineData("")]
    [InlineData("13/27")]
    [InlineData("127")]
    [InlineData("next year")]
    [InlineData("10/1999")]
    public void AnythingElse_IsNotAnExpiry(string typed) => Assert.False(ExpiryRule.TryParseTyped(typed, out _));

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NeverAsked : IPromptService
    {
        public string? AskText(string title, string message, string label, string? initialText = null) => throw new Xunit.Sdk.XunitException("asked: " + title);
        public ProductTaxPromptResult? AskProductTax(string productName, string? currentHsn, decimal? currentRate, bool currentNilRated, bool newProduct = false, ProductTaxHints? hints = null) =>
            throw new Xunit.Sdk.XunitException("HSN dialog for " + productName);
    }

    private sealed class Quiet : IConfirmationService
    {
        public bool Confirm(string message, string title) => true;
        public bool? Choose(string message, string title, string acceptLabel, string alternateLabel) => true;
        public void Notify(string title, string message) { }
        public void NotifyError(string title, string message) { }
    }

    private static ServiceProvider Services(DatabaseTestContext database) => new ServiceCollection()
        .AddLogging()
        .AddSingleton(database.Storage)
        .AddSingleton(database.BranchSettings)
        .AddSingleton<IEntitlementService, Allowed>()
        .AddSingleton<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>()
        .AddSingleton<IPromptService, NeverAsked>()
        .AddScoped(_ => database.OpenContext())
        .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
        .AddScoped<NumberSeriesService>()
        .AddScoped<StorageLocationService>()
        .AddScoped<BranchService>()
        .AddScoped<CatalogSearchService>()
        .AddScoped<PurchaseService>()
        .AddScoped<InventoryService>()
        .AddScoped<StockIntelligenceService>()
        .AddScoped(sp => new AddStockService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<PurchaseService>(), sp.GetRequiredService<IEntitlementService>(), sp.GetRequiredService<ILicenseRuntimeGuard>()))
        .AddScoped(sp => new RetailBillingService(sp.GetRequiredService<IUnitOfWork>(), sp.GetRequiredService<NumberSeriesService>(), sp.GetRequiredService<IEntitlementService>(),
            sp.GetRequiredService<CatalogSearchService>(), sp.GetRequiredService<StorageLocationService>(), sp.GetRequiredService<BranchService>(), sp.GetRequiredService<ILicenseRuntimeGuard>()))
        .BuildServiceProvider();

    private static async Task<Drug> DrugAsync(DatabaseTestContext database, string name)
    {
        var drug = new Drug { Name = name, HsnCode = "30049099", GstRate = 12m, Schedule = "G", IsActive = true };
        database.Context.Drugs.Add(drug);
        await database.Context.SaveChangesAsync();
        return drug;
    }

    [Fact]
    public void CounterItems_NeedTheDateFromThePack_OrTheTick_NeverAnInventedOne()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            await using ServiceProvider services = Services(database);
            Drug ondem = await DrugAsync(database, "Ondem 4 Tablet");
            Drug syrup = await DrugAsync(database, "Herbal Cough Syrup");
            var session = new CurrentSession();
            session.SignIn(catalog.Admin);

            var prescription = new PrescriptionParseResultDto
            {
                DetectedMedicines = [new PrescribedItemDto { DrugName = "Ondem 4 Tablet" }, new PrescribedItemDto { DrugName = "Herbal Cough Syrup" }]
            };
            var review = new PrescriptionReviewViewModel(prescription, new Dictionary<Guid, RetailStockChoice>(), new Dictionary<Guid, string>(),
                services.GetRequiredService<IServiceScopeFactory>(), session);
            PrescriptionReviewRow ondemRow = review.Rows[0];
            PrescriptionReviewRow syrupRow = review.Rows[1];
            ondemRow.ApplyLink(ondem.Id, null, ondem.Name);
            syrupRow.ApplyLink(syrup.Id, null, syrup.Name);
            ondemRow.Include = syrupRow.Include = true;
            int asked = 0;
            review.AskChoice = (_, _, _, _) => { asked++; return false; }; // "Add as counter items"
            PrescriptionReviewRow? focused = null;
            review.FocusCounterExpiry = row => focused = row;

            // No date and no tick: nothing is created, the cursor goes to that row's expiry box and the message says what to do.
            Assert.False(await review.TryAcceptAsync());
            Assert.Same(ondemRow, focused);
            Assert.Contains("type the expiry from the pack (MM/YY), or tick \"No expiry printed on pack\"", review.ErrorMessage);
            await using (PharmaBillDbContext none = database.OpenContext())
            {
                Assert.Empty(await none.Batches.Where(batch => batch.BatchNo == "OPENING").ToListAsync());
            }

            // Typed the way it is printed (1027), and the syrup's pack shows no expiry: ticked. Not asked a second time.
            ondemRow.CounterExpiry = "1027";
            syrupRow.CounterNoExpiryPrinted = true;
            Assert.True(await review.TryAcceptAsync(), review.ErrorMessage);
            Assert.Equal(1, asked);

            await using PharmaBillDbContext check = database.OpenContext();
            Batch ondemBatch = await check.Batches.AsNoTracking().SingleAsync(batch => batch.DrugId == ondem.Id);
            Batch syrupBatch = await check.Batches.AsNoTracking().SingleAsync(batch => batch.DrugId == syrup.Id);
            Assert.Equal((new DateOnly(2027, 10, 31), (NoExpiryReason?)null), (ondemBatch.ExpiryDate, ondemBatch.NoExpiryReason));
            Assert.Equal(((DateOnly?)null, (NoExpiryReason?)NoExpiryReason.NotPrintedOnPack), (syrupBatch.ExpiryDate, syrupBatch.NoExpiryReason));
            Assert.Equal(2, review.GetConfirmedItems().Count);
        });
    }

    [Fact]
    public void OlderBatchesWithoutExpiry_AreFlaggedCounted_AndGivenADateOnTheStockPage()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            await using ServiceProvider services = Services(database);
            Drug drug = await DrugAsync(database, "Old Syrup 100ml");
            Drug thermometer = await DrugAsync(database, "Clinical Thermometer");
            thermometer.HsnCode = "9025";
            // As they exist today: stored before the rule (or arrived through sync), no date and no reason.
            database.Context.IsApplyingSync = true;
            var old = new Batch { DrugId = drug.Id, BatchNo = "OLD-1", Quantity = 4m };
            var instrument = new Batch { DrugId = thermometer.Id, BatchNo = "TH-1", Quantity = 2m };
            database.Context.Batches.AddRange(old, instrument);
            database.Context.StockMovements.AddRange(
                new StockMovement { BatchId = old.Id, DrugId = drug.Id, QuantityChange = 4m, MovementType = "Opening" },
                new StockMovement { BatchId = instrument.Id, DrugId = thermometer.Id, QuantityChange = 2m, MovementType = "Opening" });
            await database.Context.SaveChangesAsync();
            database.Context.IsApplyingSync = false;

            var session = new CurrentSession();
            session.SignIn(catalog.Admin);
            var stock = new StockPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, new UserSessionService(session), new Quiet(), null!, null!, new TabularExportService(), null!, null!);
            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = stock, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(StockPageViewModel))], Resources = mainWindow.Resources };
            await stock.LoadAsync();
            await Ui.SettleAsync();

            // Flagged and counted; the thermometer (chapter 90) is not, it needs no date.
            Assert.Equal(1, stock.BatchesNeedingExpiry);
            Assert.True(Ui.Named<Border>(host, "Batches with no expiry").IsVisible);
            Assert.Equal("No expiry", stock.Rows.Single(row => row.BatchNo == "OLD-1").Status);
            Assert.Equal("None (instrument)", stock.Rows.Single(row => row.BatchNo == "TH-1").ExpiryLabel);

            await Ui.PressAsync(Ui.Button(host, "Show them"));
            await Ui.SettleAsync();
            StockRowViewModel row = Assert.Single(stock.Rows);
            Assert.Equal("OLD-1", row.BatchNo);

            // Given its date in the row, as printed on the pack.
            Ui.Type(Ui.Named<TextBox>(host, "Batch expiry"), "03/28");
            await Ui.PressAsync(Ui.Single<Button>(host, button => button.IsVisible && Equals(button.Content, "Save") && button.DataContext == row));
            Assert.True(string.IsNullOrEmpty(stock.ErrorMessage), stock.ErrorMessage);
            Assert.Equal(0, stock.BatchesNeedingExpiry);
            Assert.Equal("03/2028", row.ExpiryLabel);
            await using PharmaBillDbContext check = database.OpenContext();
            Assert.Equal(new DateOnly(2028, 3, 31), (await check.Batches.AsNoTracking().SingleAsync(batch => batch.Id == old.Id)).ExpiryDate);
            Assert.True(await check.AuditLogs.AnyAsync(log => log.Action == "BatchExpirySet" && log.EntityId == old.Id));
            host.Close();
        });
    }
}
