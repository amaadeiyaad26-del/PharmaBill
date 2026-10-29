using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// No expiry only for durable instruments (HSN chapter 90), or when the user ticked "No expiry printed on pack"; the
// reason is recorded on the batch, and a batch without either can never be saved.
public sealed class ExpiryRuleTests
{
    [Theory]
    [InlineData("9025", true)]      // thermometer
    [InlineData("90189011", true)]  // BP monitor
    [InlineData("9019", true)]      // nebuliser
    [InlineData("3005", false)]     // dressings, bandages
    [InlineData("3822", false)]     // test strips
    [InlineData("4014", false)]     // condoms
    [InlineData("3004", false)]     // medicines
    [InlineData("8423", false)]     // weighing machines outside chapter 90
    [InlineData(null, false)]
    public void OnlyChapter90_IsADurableInstrument(string? hsn, bool durable) =>
        Assert.Equal(durable, ExpiryRule.IsDurableInstrument(hsn));

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static async Task<PurchaseInvoice> BuyAsync(DatabaseTestContext database, E2EHealthSeed.Catalog catalog, Drug drug, string batch, DateOnly? expiry, bool ticked, string invoiceNo)
    {
        var line = new PurchaseLineInput(drug.Id, batch, expiry, 10m, 0m, 50m, 40m, drug.GstRate ?? 12m, 0m, 400m, null) { NoExpiryPrinted = ticked };
        decimal tax = decimal.Round(400m * (drug.GstRate ?? 12m) / 100m, 2);
        return await database.CreatePurchaseService(new Allowed()).SavePurchaseAsync(
            new SavePurchaseInput(catalog.Suppliers[0].Id, invoiceNo, PharmaBill.Core.BusinessClock.Today, 400m, 0m, tax, 400m + tax, [line]),
            catalog.Admin.Id, UserRole.Owner);
    }

    private static async Task<Drug> DrugAsync(DatabaseTestContext database, string name, string hsn, decimal gst)
    {
        var drug = new Drug { Name = name, HsnCode = hsn, GstRate = gst, IsActive = true };
        database.Context.Drugs.Add(drug);
        await database.Context.SaveChangesAsync();
        return drug;
    }

    [Fact]
    public async Task APurchaseLineWithoutExpiry_IsRefused_UnlessAnInstrumentOrTicked_AndTheReasonIsRecorded()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);
        Drug strips = await DrugAsync(database, "Glucose Test Strips", "3822", 12m);
        Drug monitor = await DrugAsync(database, "BP Monitor", "9018", 12m);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => BuyAsync(database, catalog, strips, "TS1", null, ticked: false, "E-1"));
        Assert.Contains("'Glucose Test Strips' needs an expiry date", refused.Message);
        Assert.Contains(ExpiryRule.NoExpiryPrintedLabel, refused.Message);

        await BuyAsync(database, catalog, strips, "TS1", null, ticked: true, "E-2");
        await BuyAsync(database, catalog, monitor, "BPM1", null, ticked: false, "E-3");
        await BuyAsync(database, catalog, strips, "TS2", new DateOnly(2028, 1, 31), ticked: true, "E-4");

        await using var check = database.OpenContext();
        Assert.Equal((null, NoExpiryReason.NotPrintedOnPack), await Batch(check, "TS1"));
        Assert.Equal((null, NoExpiryReason.DurableInstrument), await Batch(check, "BPM1"));
        // A date wins over the tick: the batch has its expiry and no reason.
        Assert.Equal((new DateOnly(2028, 1, 31), null), await Batch(check, "TS2"));
        PurchaseItem item = await check.PurchaseItems.AsNoTracking().SingleAsync(row => row.BatchNo == "TS1");
        Assert.Equal(NoExpiryReason.NotPrintedOnPack, item.NoExpiryReason);
    }

    private static async Task<(DateOnly?, NoExpiryReason?)> Batch(PharmaBill.Data.Persistence.PharmaBillDbContext context, string batchNo)
    {
        Batch batch = await context.Batches.AsNoTracking().SingleAsync(row => row.BatchNo == batchNo);
        return (batch.ExpiryDate, batch.NoExpiryReason);
    }

    [Fact]
    public async Task ABatchWithNoExpiryAndNoReason_CanNeverBeSaved_FromAnyPath()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);
        Drug drug = await DrugAsync(database, "Paracetamol 500", "3004", 12m);

        database.Context.Batches.Add(new Batch { DrugId = drug.Id, BatchNo = "BLANK1", Quantity = 5m });
        var refused = Assert.Throws<InvalidOperationException>(() => database.Context.SaveChanges());
        Assert.Contains("'Batch BLANK1' needs an expiry date", refused.Message);
        database.Context.ChangeTracker.Clear();

        // Clearing the date on an existing batch is refused the same way.
        var dated = new Batch { DrugId = drug.Id, BatchNo = "DATED1", ExpiryDate = new DateOnly(2028, 6, 30), Quantity = 5m };
        database.Context.Batches.Add(dated);
        await database.Context.SaveChangesAsync();
        dated.ExpiryDate = null;
        Assert.Throws<InvalidOperationException>(() => database.Context.SaveChanges());
        database.Context.ChangeTracker.Clear();

        // With a recorded reason it saves.
        database.Context.Batches.Add(new Batch { DrugId = drug.Id, BatchNo = "TICKED1", Quantity = 5m, NoExpiryReason = NoExpiryReason.NotPrintedOnPack });
        await database.Context.SaveChangesAsync();
    }

    [Fact]
    public void AddStockForm_ABlankExpiry_IsRefused_UntilTheTickIsSet()
    {
        var form = new PharmaBill.App.ViewModels.AddStockViewModel(null, null, new BackupPageEndToEndTests.AlwaysYes(), null) { ExpiryText = string.Empty };
        Assert.True(form.ShowNoExpiryTick);
        form.Validate();
        Assert.Contains("If the pack genuinely shows none, tick \"No expiry printed on pack\"", form.ExpiryError);

        form.NoExpiryPrinted = true;
        form.Validate();
        Assert.Equal(string.Empty, form.ExpiryError);

        // Typing a date hides the tick: a date always wins.
        form.ExpiryText = "08/2028";
        Assert.False(form.ShowNoExpiryTick);
    }
}
