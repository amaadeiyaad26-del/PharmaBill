using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Stock entry that would change a medicine's statutory schedule: refused without an override; with an Admin's
// PIN-verified override it saves, changes the schedule billing reads, and leaves an audit entry. An existing medicine
// opens on its saved schedule, so ordinary stock entry never runs into the override at all.
public sealed class ScheduleOverrideTests
{
    private sealed class Entitlements : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakePrompt(Func<ScheduleChangeRequiredException, ScheduleChangeAuthorization?> answer) : IScheduleOverridePrompt
    {
        public List<ScheduleChangeRequiredException> Asked { get; } = [];

        public Task<ScheduleChangeAuthorization?> RequestAsync(ScheduleChangeRequiredException block, CancellationToken cancellationToken = default)
        {
            Asked.Add(block);
            return Task.FromResult(answer(block));
        }
    }

    private static AddStockService Create(DatabaseTestContext database)
    {
        var uow = new UnitOfWork(database.Context);
        var entitlements = new Entitlements();
        return new AddStockService(uow, database.CreatePurchaseService(entitlements, uow), entitlements, new NullLicenseRuntimeGuard());
    }

    private static AddStockInput Input(Guid supplierId, Guid? drugId, string schedule, string invoice) =>
        new(drugId, null, "Clonazepam 0.5", "Clonazepam", "Acme", schedule, "B-" + invoice,
            BusinessClock.Today.AddYears(1), 30m, 20m, 10m, 0m, 10m, 12m, supplierId, invoice, BusinessClock.Today, "R1", HsnCode: "30049099");

    private static async Task<AppUser> UserAsync(DatabaseTestContext database, string name, UserRole role)
    {
        var user = new AppUser { UserName = name, DisplayName = name, Role = role, PasswordHash = PasswordHasher.Hash("1234"), IsActive = true };
        database.Context.AppUsers.Add(user);
        await database.Context.SaveChangesAsync();
        return user;
    }

    // A medicine saved as H, with stock already in: the starting point of every test here.
    private static async Task<(Supplier Supplier, Guid DrugId)> SavedAsHAsync(DatabaseTestContext database)
    {
        Supplier supplier = await Create(database).AddSupplierAsync("Sup", Guid.NewGuid(), UserRole.Owner);
        AddStockResult first = await Create(database).AddStockAsync(Input(supplier.Id, null, "H", "INV-1"), Guid.NewGuid(), UserRole.Owner);
        return (supplier, first.DrugId);
    }

    private static Task<decimal> StockAsync(DatabaseTestContext database, Guid drugId) =>
        database.Context.StockMovements.Where(m => database.Context.Batches.Any(b => b.Id == m.BatchId && b.DrugId == drugId)).SumAsync(m => m.QuantityChange);

    [Fact]
    public async Task ChangingTheSchedule_WithoutAnOverride_IsRefusedAndNothingIsSaved()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (supplier, drugId) = await SavedAsHAsync(database);

        var block = await Assert.ThrowsAsync<ScheduleChangeRequiredException>(() =>
            Create(database).AddStockAsync(Input(supplier.Id, drugId, "H1", "INV-2"), Guid.NewGuid(), UserRole.Owner));

        Assert.Equal(("H", "H1", drugId), (block.SavedSchedule, block.RequestedSchedule, block.DrugId));
        Assert.False(block.LowersControls);
        Assert.Contains("schedule override", block.Message);
        database.Context.ChangeTracker.Clear();
        Assert.Equal("H", (await database.Context.Drugs.SingleAsync()).Schedule);
        Assert.Equal(10m, await StockAsync(database, drugId));
        Assert.Empty(await database.Context.ScheduleOverrides.ToListAsync());
    }

    [Fact]
    public async Task AnAdminAuthorizedOverride_SavesTheStock_ChangesTheSchedule_AndIsAudited()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (supplier, drugId) = await SavedAsHAsync(database);
        AppUser admin = await UserAsync(database, "owner1", UserRole.Owner);
        Guid enteredBy = Guid.NewGuid();

        await Create(database).AddStockAsync(
            Input(supplier.Id, drugId, "H1", "INV-2") with { ScheduleChange = new ScheduleChangeAuthorization(admin.Id, "Pack label shows Schedule H1") },
            enteredBy, UserRole.Owner);

        database.Context.ChangeTracker.Clear();
        Assert.Equal(20m, await StockAsync(database, drugId));
        Assert.Equal("H1", (await database.Context.Drugs.SingleAsync()).Schedule);
        ScheduleOverride record = await database.Context.ScheduleOverrides.SingleAsync();
        Assert.Equal((drugId, "H1", "Pack label shows Schedule H1"), (record.DrugId, record.Schedule, record.Reason));
        Assert.NotNull(record.EffectiveFromUtc);
        AuditLog audit = await database.Context.AuditLogs.SingleAsync(a => a.Action == "ScheduleOverride");
        Assert.Equal((enteredBy, drugId), (audit.UserId, audit.EntityId));
        Assert.Contains("schedule H changed to H1", audit.Details);
        Assert.Contains("Authorized by owner1 (Admin)", audit.Details);
        Assert.Contains("Pack label shows Schedule H1", audit.Details);
        Assert.InRange(audit.ActionAtUtc, BusinessClock.UtcNow.AddMinutes(-5), BusinessClock.UtcNow);

        // From now on the medicine is H1 everywhere: the next entry opens on H1, and H1 saves without an override.
        Assert.Equal("H1", await Create(database).SavedScheduleAsync(drugId, null));
        await Create(database).AddStockAsync(Input(supplier.Id, drugId, "H1", "INV-3"), enteredBy, UserRole.Owner);
        Assert.Throws<ScheduleChangeRequiredException>(() =>
            Create(database).AddStockAsync(Input(supplier.Id, drugId, "H", "INV-4"), enteredBy, UserRole.Owner).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task OnlyAnAdmin_WithAReason_CanAuthorizeAnOverride()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (supplier, drugId) = await SavedAsHAsync(database);
        AppUser pharmacist = await UserAsync(database, "pharm1", UserRole.Pharmacist);
        AppUser admin = await UserAsync(database, "owner1", UserRole.Owner);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(database).AddStockAsync(
            Input(supplier.Id, drugId, "OTC", "INV-2") with { ScheduleChange = new ScheduleChangeAuthorization(pharmacist.Id, "Label") }, Guid.NewGuid(), UserRole.Owner));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Create(database).AddStockAsync(
            Input(supplier.Id, drugId, "OTC", "INV-3") with { ScheduleChange = new ScheduleChangeAuthorization(Guid.NewGuid(), "Label") }, Guid.NewGuid(), UserRole.Owner));
        var noReason = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(database).AddStockAsync(
            Input(supplier.Id, drugId, "OTC", "INV-4") with { ScheduleChange = new ScheduleChangeAuthorization(admin.Id, "  ") }, Guid.NewGuid(), UserRole.Owner));
        Assert.Contains("reason", noReason.Message);

        database.Context.ChangeTracker.Clear();
        Assert.Equal("H", (await database.Context.Drugs.SingleAsync()).Schedule);
        Assert.Equal(10m, await StockAsync(database, drugId));
        Assert.Empty(await database.Context.ScheduleOverrides.ToListAsync());
        Assert.True(ScheduleChangeAuthorization.LowersControls("H", "OTC"));
        Assert.True(ScheduleChangeAuthorization.CanAuthorize(UserRole.Manager));
        Assert.False(ScheduleChangeAuthorization.CanAuthorize(UserRole.Pharmacist));
    }

    // The false block: a medicine saved as H whose catalog entry says G was suggested as G, and saving G was refused.
    [Fact]
    public async Task AnExistingMedicine_IsSuggestedItsSavedSchedule_NotTheCatalogs()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (supplier, drugId) = await SavedAsHAsync(database);
        database.Context.CatalogInfos.Add(new CatalogInfo { NameKey = "clonazepam 0.5", Schedule = "G" });
        await database.Context.SaveChangesAsync();

        Assert.Equal("H", await Create(database).SuggestScheduleAsync(drugId, null, "Clonazepam 0.5"));
        Assert.Equal("G", await Create(database).SuggestScheduleAsync(null, null, "Clonazepam 0.5"));
        Assert.Null(await Create(database).SavedScheduleAsync(null, null));

        // An override record (from sync) wins over the medicine's own schedule, as it does at billing.
        database.Context.ScheduleOverrides.Add(new ScheduleOverride { DrugId = drugId, Schedule = "H1", EffectiveFromUtc = BusinessClock.UtcNow.AddDays(-1) });
        await database.Context.SaveChangesAsync();
        Assert.Equal("H1", await Create(database).SavedScheduleAsync(drugId, null));
        await Create(database).AddStockAsync(Input(supplier.Id, drugId, "H1", "INV-2"), Guid.NewGuid(), UserRole.Owner);
    }

    [Fact]
    public async Task TheFlow_AsksForTheOverride_SavesWhenAuthorized_AndNothingWhenDeclined()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (supplier, drugId) = await SavedAsHAsync(database);
        AppUser admin = await UserAsync(database, "owner1", UserRole.Owner);
        var user = new AppUser { UserName = "owner2", Role = UserRole.Owner };

        var declined = new FakePrompt(_ => null);
        var refused = await Assert.ThrowsAsync<ScheduleOverrideDeclinedException>(() =>
            ScheduleOverrideFlow.AddStockAsync(Create(database), Input(supplier.Id, drugId, "H1", "INV-2"), user, declined));
        Assert.Single(declined.Asked);
        Assert.Contains("stays schedule H", refused.Message);
        Assert.Equal(10m, await StockAsync(database, drugId));

        var approved = new FakePrompt(_ => new ScheduleChangeAuthorization(admin.Id, "Supplier invoice lists H1"));
        await ScheduleOverrideFlow.AddStockAsync(Create(database), Input(supplier.Id, drugId, "H1", "INV-3"), user, approved);
        Assert.Equal(("H", "H1"), (approved.Asked.Single().SavedSchedule, approved.Asked.Single().RequestedSchedule));
        Assert.Equal(20m, await StockAsync(database, drugId));

        // No schedule change: the prompt is never shown.
        var untouched = new FakePrompt(_ => throw new InvalidOperationException("not expected"));
        await ScheduleOverrideFlow.AddStockAsync(Create(database), Input(supplier.Id, drugId, "H1", "INV-4"), user, untouched);
        Assert.Empty(untouched.Asked);
    }

    // The Add Stock form: opens on the saved schedule; choosing another asks for the override and then saves.
    [Fact]
    public void AddStockForm_OpensOnTheSavedSchedule_AndSavesAChangeOnlyThroughTheOverride()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var (supplier, drugId) = await SavedAsHAsync(database);
            AppUser admin = await UserAsync(database, "owner1", UserRole.Owner);
            ScheduleChangeAuthorization? answer = null;
            var prompt = new FakePrompt(_ => answer);

            var services = new ServiceCollection();
            services.AddSingleton(database.Context);
            services.AddSingleton<IEntitlementService>(new Entitlements());
            services.AddTransient(_ => Create(database));
            services.AddSingleton<IScheduleOverridePrompt>(prompt);
            await using ServiceProvider provider = services.BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(admin);

            var form = new AddStockViewModel(provider.GetRequiredService<IServiceScopeFactory>(), session, new BackupPageEndToEndTests.AlwaysYes(), null);
            await form.InitializeAsync(new AddStockRequest(drugId, null, "Clonazepam 0.5", "Clonazepam", "Acme"));
            Assert.Equal("H", form.Schedule);
            Assert.Contains("Admin schedule override", form.ScheduleHint);

            var saved = new List<AddStockResult>();
            form.Saved += (_, result) => saved.Add(result);
            form.BatchNo = "B-9";
            form.ExpiryText = BusinessClock.Today.AddYears(1).ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
            form.MrpText = "30";
            form.PurchaseRateText = "20";
            form.QuantityText = "5";
            form.GstText = "12";
            form.SelectedSupplier = form.Suppliers.Single(s => s.Id == supplier.Id);
            form.SupplierInvoiceNo = "INV-9";
            form.InvoiceDate = BusinessClock.Today.ToDateTime(TimeOnly.MinValue);

            // Declined (cancelled, or the PIN not verified): back on the form, nothing saved, and it says why.
            form.Schedule = "H1";
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Empty(saved);
            Assert.Contains("schedule override was not authorized", form.ErrorMessage);
            Assert.Equal(10m, await StockAsync(database, drugId));

            // Authorized: saved, and the medicine is now H1.
            answer = new ScheduleChangeAuthorization(admin.Id, "Pack label shows Schedule H1");
            await form.SaveCommand.ExecuteAsync(null);
            Assert.Single(saved);
            Assert.Equal(15m, await StockAsync(database, drugId));
            Assert.Equal(2, prompt.Asked.Count);
            Assert.Equal("H1", await Create(database).SavedScheduleAsync(drugId, null));
        });
    }

    // The dialog: says what changes, warns when the new schedule is less controlled, and needs a reason.
    [Fact]
    public void OverrideDialog_NeedsAReason_AndWarnsWhenControlsAreLowered()
    {
        Ui.Run(async () =>
        {
            var dialog = new ScheduleOverrideWindow(new ScheduleChangeRequiredException(Guid.NewGuid(), "Clonazepam 0.5", "H1", "OTC"));
            Ui.Prepare(dialog);
            bool? result = null;
            dialog.Loaded += async (_, _) =>
            {
                await Ui.SettleAsync();
                Assert.Contains(Texts(dialog), t => t.Contains("Schedule Override Required"));
                Assert.Contains(Texts(dialog), t => t.Contains("statutory schedule rules (Schedule H1/OTC). An override is required to proceed."));
                Assert.Contains(Texts(dialog), t => t.Contains("less controlled than H1"));

                Button overrideButton = Ui.Button(dialog, "Override Schedule");
                await Ui.PressAsync(overrideButton);
                Assert.True(dialog.IsVisible, "closed without a reason");
                Assert.Contains(Texts(dialog), t => t.Contains("Give a reason"));

                Ui.Type(Ui.Named<TextBox>(dialog, "Schedule override reason"), "Pack label shows OTC");
                await Ui.PressAsync(overrideButton);
            };
            result = dialog.ShowDialog();
            Assert.True(result);
            Assert.Equal("Pack label shows OTC", dialog.Reason);

            var raising = new ScheduleOverrideWindow(new ScheduleChangeRequiredException(Guid.NewGuid(), "Clonazepam 0.5", "H", "H1"));
            Assert.DoesNotContain(Texts(raising), t => t.Contains("less controlled"));
        });
    }

    private static IEnumerable<string> Texts(System.Windows.DependencyObject root) =>
        System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>().SelectMany(Texts)
            .Concat(root is TextBlock { IsVisible: true } block && !string.IsNullOrEmpty(block.Text) ? [block.Text] : []);
}
