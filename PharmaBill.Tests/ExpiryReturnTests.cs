using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Expiry returns to distributors: what is claimable, held stock that cannot be sold, sent stock that is owed, credit
// notes against the supplier ledger, rejections, voiding, and two counters racing for the same strip.
public sealed class ExpiryReturnTests
{
    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed record Shop(E2EHealthSeed.Catalog Catalog, Supplier Supplier, Drug Syrup, Batch Near, Batch Far, Drug Tablet, Batch NearTablet);

    // A distributor with a 90-before / 30-after policy; a purchase brings in a batch expiring in 30 days, one in 200 days,
    // and a second near-expiry product.
    private static async Task<Shop> SetUpAsync(DatabaseTestContext database, string supplierGstin = "01AAAAA1111A1Z5")
    {
        E2EHealthSeed.Catalog catalog = await E2EHealthSeed.SeedAsync(database.Context);
        var supplier = new Supplier { Name = "Valley Pharma Distributors", Gstin = supplierGstin, Address = "Industrial Area, Srinagar", ExpiryReturnDaysBefore = 90, ExpiryReturnDaysAfter = 30, ExpiryReturnPolicyNote = "Expiry accepted 3 months before to 1 month after, at purchase rate." };
        var syrup = new Drug { Name = "Cough Syrup 100ml", Unit = "100 ML", HsnCode = "30049099", GstRate = 12m };
        var tablet = new Drug { Name = "Antacid Tablet", Unit = "10 TAB", HsnCode = "30049011", GstRate = 12m };
        database.Context.AddRange(supplier, syrup, tablet);
        await database.Context.SaveChangesAsync();
        DateOnly today = BusinessClock.Today;
        // Syrup: 20 at 50 (1000), 10 at 50 (500); tablet: 30 at 20 less 60 discount (540). GST 12%.
        await database.CreatePurchaseService(new Allowed()).SavePurchaseAsync(new SavePurchaseInput(supplier.Id, "VPD-7781", today.AddDays(-200), 2040m, 60m, 244.8m, 2284.8m,
            [
                new PurchaseLineInput(syrup.Id, "SYR-NEAR", today.AddDays(30), 20m, 0m, 90m, 50m, 12m, 0m, 1000m, "A1"),
                new PurchaseLineInput(syrup.Id, "SYR-FAR", today.AddDays(200), 10m, 0m, 90m, 50m, 12m, 0m, 500m, "A1"),
                new PurchaseLineInput(tablet.Id, "TAB-NEAR", today.AddDays(10), 30m, 0m, 40m, 20m, 12m, 60m, 540m, "A2")
            ]), catalog.Admin.Id, UserRole.Owner);
        Batch near = await database.Context.Batches.SingleAsync(batch => batch.BatchNo == "SYR-NEAR");
        Batch far = await database.Context.Batches.SingleAsync(batch => batch.BatchNo == "SYR-FAR");
        Batch nearTablet = await database.Context.Batches.SingleAsync(batch => batch.BatchNo == "TAB-NEAR");
        return new Shop(catalog, supplier, syrup, near, far, tablet, nearTablet);
    }

    private static ExpiryReturnService Service(DatabaseTestContext database, PharmaBillDbContext? context = null)
    {
        var uow = new UnitOfWork(context ?? database.Context);
        return new ExpiryReturnService(uow, new NumberSeriesService(uow), new Allowed());
    }

    private static async Task<decimal> OnHandAsync(PharmaBillDbContext context, Guid batchId) =>
        (await context.StockMovements.Where(movement => movement.BatchId == batchId).SumAsync(movement => (decimal?)movement.QuantityChange)) ?? 0m;

    [Theory]
    [InlineData(-91, false)]
    [InlineData(-90, true)]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void Window_IncludesBothBoundaryDays(int todayRelativeToExpiry, bool accepted)
    {
        var expiry = new DateOnly(2026, 12, 31);
        Assert.Equal(accepted, new ExpiryReturnWindow(90, 30).Accepts(expiry, expiry.AddDays(todayRelativeToExpiry)));
    }

    // The window turns on the business date in Asia/Kolkata: 00:10 IST on 22 Oct is still 21 Oct in UTC.
    [Fact]
    public async Task Claimable_FollowsTheIndianBusinessDate_AtTheBoundary()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        DateOnly expiry = shop.Far.ExpiryDate!.Value;
        // 90 days before expiry, at 23:50 IST the evening before: not yet claimable.
        DateTime eveningBefore = BusinessClock.FromLocal(expiry.AddDays(-91).ToDateTime(new TimeOnly(23, 50)));
        using (BusinessClock.Use(new FixedClock(eveningBefore)))
        {
            Assert.DoesNotContain(await Service(database).GetClaimableAsync(), row => row.BatchId == shop.Far.Id);
        }
        // Twenty minutes later, 00:10 IST on the first day of the window (still the previous day in UTC): claimable.
        DateTime justAfterMidnight = BusinessClock.FromLocal(expiry.AddDays(-90).ToDateTime(new TimeOnly(0, 10)));
        Assert.NotEqual(DateOnly.FromDateTime(justAfterMidnight), expiry.AddDays(-90));
        using (BusinessClock.Use(new FixedClock(justAfterMidnight)))
        {
            Assert.Contains(await Service(database).GetClaimableAsync(), row => row.BatchId == shop.Far.Id);
        }
    }

    [Fact]
    public async Task Claimable_ListsBatchesInsideTheirSuppliersWindow_WithThePurchaseRate()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        IReadOnlyList<ClaimableBatchRow> rows = await Service(database).GetClaimableAsync();

        ClaimableBatchRow near = Assert.Single(rows, row => row.BatchId == shop.Near.Id);
        Assert.Equal((shop.Supplier.Id, true, 20m, 50m, 12m, 1000m, "VPD-7781"), (near.SupplierId, near.Traceable, near.Available, near.Rate, near.GstRate, near.ValueAtRate, near.PurchaseInvoiceNo));
        // Discount is part of what was paid: 540 for 30 is 18 a unit, not the 20 list rate.
        Assert.Equal(18m, Assert.Single(rows, row => row.BatchId == shop.NearTablet.Id).Rate);
        Assert.DoesNotContain(rows, row => row.BatchId == shop.Far.Id);
    }

    [Fact]
    public async Task ABatchWithNoTraceablePurchase_IsListedWithoutASupplier_AndClaimedFromTheOneChosen()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        // E2E seed batches have no purchase behind them.
        Batch untraced = shop.Catalog.CetirizineBatch;
        untraced.ExpiryDate = BusinessClock.Today.AddDays(20);
        await database.Context.SaveChangesAsync();
        ClaimableBatchRow row = Assert.Single(await Service(database).GetClaimableAsync(), item => item.BatchId == untraced.Id);
        Assert.Equal((false, null, ""), (row.Traceable, row.SupplierId, row.SupplierName));

        ExpiryClaim claim = await Service(database).CreateDraftAsync(shop.Supplier.Id, [new(untraced.Id, 5m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        Assert.Null((await database.Context.ExpiryClaimLines.SingleAsync(line => line.ExpiryClaimId == claim.Id)).PurchaseItemId);
    }

    [Fact]
    public async Task HeldStock_CannotBeSold_AndStaysInValuationAsHeld()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryClaim claim = await Service(database).CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 15m)], null, shop.Catalog.Admin.Id, UserRole.Owner);

        Assert.Equal("HC-EXP/2026-27/000001", claim.ClaimNo);
        Assert.Equal(5m, await OnHandAsync(database.Context, shop.Near.Id));
        RetailBillingService billing = database.CreateRetailBillingService(new Allowed());
        InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => billing.SaveSaleAsync(new SaveRetailSaleInput("Walk-in", "9000000001", null, null, null, null,
            [new RetailSaleLineInput(shop.Syrup.Id, shop.Near.Id, 6m, 0m, 90m)], [new RetailPaymentInput("Cash", 540m)], false), shop.Catalog.Admin.Id, UserRole.Owner));
        Assert.Contains("exceeds available stock", refused.Message);
        // The 5 left are still sellable.
        await billing.SaveSaleAsync(new SaveRetailSaleInput("Walk-in", "9000000001", null, null, null, null,
            [new RetailSaleLineInput(shop.Syrup.Id, shop.Near.Id, 5m, 0m, 90m)], [new RetailPaymentInput("Cash", 450m)], false), shop.Catalog.Admin.Id, UserRole.Owner);

        HeldStockRow held = Assert.Single(await Service(database).GetHeldStockAsync());
        Assert.Equal((shop.Near.Id, 15m, 750m, claim.ClaimNo), (held.BatchId, held.Quantity, held.Value, held.ClaimNo));
        Assert.Contains(await new StockIntelligenceService(database.Context).GetValuationRowsAsync(), row => row.BatchNo == "SYR-NEAR" && row.IsHeldForReturn && row.Quantity == 15m);
    }

    [Fact]
    public async Task SentStock_LeavesStockInHand_ButIsOwed()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        ExpiryClaim claim = await service.CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 20m), new(shop.NearTablet.Id, 30m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        int movements = await database.Context.StockMovements.CountAsync();
        await service.MarkSentAsync(claim.Id, BusinessClock.Today, "Courier", "Returns desk", shop.Catalog.Admin.Id, UserRole.Owner);

        Assert.Equal(0m, await OnHandAsync(database.Context, shop.Near.Id));
        Assert.Equal(movements, await database.Context.StockMovements.CountAsync());
        Assert.Empty(await service.GetHeldStockAsync());
        Assert.DoesNotContain(await new StockIntelligenceService(database.Context).GetValuationRowsAsync(), row => row.BatchNo == "SYR-NEAR");
        ExpiryClaimSummary open = Assert.Single(await service.GetClaimsAsync(open: true));
        // 20 x 50 + 12% = 1120; 30 x 18 + 12% = 604.80.
        Assert.Equal((ExpiryClaimStatus.Sent, 1724.80m, 1724.80m, 0), (open.Status, open.TotalValue, open.Balance, open.DaysOutstanding));
        ExpiryClaim saved = await database.Context.ExpiryClaims.AsNoTracking().SingleAsync();
        Assert.Equal((1540m, 92.40m, 92.40m, 0m), (saved.TaxableValue, saved.CgstAmount, saved.SgstAmount, saved.IgstAmount));
    }

    [Fact]
    public async Task APartCredit_LeavesTheRightBalance_AndReducesTheSupplierPayable()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        ExpiryClaim claim = await service.CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 20m), new(shop.NearTablet.Id, 30m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        await service.MarkSentAsync(claim.Id, BusinessClock.Today, "By hand", null, shop.Catalog.Admin.Id, UserRole.Owner);
        List<ExpiryClaimLine> lines = await database.Context.ExpiryClaimLines.AsNoTracking().Where(line => line.ExpiryClaimId == claim.Id).ToListAsync();
        ExpiryClaimLine syrup = lines.Single(line => line.BatchNo == "SYR-NEAR");
        ExpiryClaimLine tablet = lines.Single(line => line.BatchNo == "TAB-NEAR");
        decimal payableBefore = await PayableAsync(database, shop.Supplier.Id);

        // 1000 against the syrup (120 short: the distributor paid ex-GST, accepted); nothing yet on the tablet.
        await service.RecordCreditNoteAsync(claim.Id, new ExpiryCreditInput("CN-VPD-55", BusinessClock.Today, 1000m, 892.86m, 53.57m, 53.57m, 0m,
            ExpiryCreditSettlement.AdjustedAgainstPurchases, [new(syrup.Id, 1000m, "Credited without GST")]), shop.Catalog.Admin.Id, UserRole.Owner);

        ExpiryClaimSummary open = Assert.Single(await service.GetClaimsAsync(open: true));
        Assert.Equal((ExpiryClaimStatus.PartCredited, 1000m, 604.80m), (open.Status, open.CreditedValue, open.Balance));
        ExpiryClaimLine credited = await database.Context.ExpiryClaimLines.AsNoTracking().SingleAsync(line => line.Id == syrup.Id);
        Assert.Equal((1000m, 120m, "Credited without GST"), (credited.CreditedValue, credited.ShortfallValue, credited.ShortfallReason));
        Assert.Equal(payableBefore - 1000m, await PayableAsync(database, shop.Supplier.Id));
        SupplierLedgerEntry entry = await database.Context.SupplierLedgerEntries.SingleAsync(item => item.EntryType == LedgerEntryTypes.ExpiryClaimCredit);
        Assert.Equal((1000m, 0m, "CN-VPD-55"), (entry.Debit, entry.Credit, entry.ReferenceNo));
        // The distributor's GST is kept as printed on its credit note, beside the claim's own.
        ExpiryClaimCredit note = await database.Context.ExpiryClaimCredits.SingleAsync();
        Assert.Equal((892.86m, 53.57m, 53.57m), (note.TaxableValue, note.CgstAmount, note.SgstAmount));

        // The rest in cash: the claim closes, the ledger is not touched again.
        await service.RecordCreditNoteAsync(claim.Id, new ExpiryCreditInput("CN-VPD-61", BusinessClock.Today, 604.80m, 540m, 32.40m, 32.40m, 0m,
            ExpiryCreditSettlement.Cash, [new(tablet.Id, 604.80m, null)]), shop.Catalog.Admin.Id, UserRole.Owner);
        Assert.Empty(await service.GetClaimsAsync(open: true));
        Assert.Equal(ExpiryClaimStatus.Credited, Assert.Single(await service.GetClaimsAsync(open: false)).Status);
        Assert.Equal(payableBefore - 1000m, await PayableAsync(database, shop.Supplier.Id));
    }

    [Fact]
    public async Task ACreditNoteMustAddUp_AndCannotExceedWhatIsOwed()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        ExpiryClaim claim = await service.CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 20m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        await service.MarkSentAsync(claim.Id, BusinessClock.Today, "By hand", null, shop.Catalog.Admin.Id, UserRole.Owner);
        Guid line = (await database.Context.ExpiryClaimLines.SingleAsync()).Id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordCreditNoteAsync(claim.Id, new ExpiryCreditInput("X1", BusinessClock.Today, 500m, 0m, 0m, 0m, 0m, ExpiryCreditSettlement.Cash, [new(line, 400m, null)]), shop.Catalog.Admin.Id, UserRole.Owner));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordCreditNoteAsync(claim.Id, new ExpiryCreditInput("X2", BusinessClock.Today, 1200m, 0m, 0m, 0m, 0m, ExpiryCreditSettlement.Cash, [new(line, 1200m, null)]), shop.Catalog.Admin.Id, UserRole.Owner));
        Assert.Empty(await database.Context.ExpiryClaimCredits.ToListAsync());
    }

    [Fact]
    public async Task ARejectedLineWrittenOff_ReducesNothingTwice()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        ExpiryClaim claim = await service.CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 20m), new(shop.NearTablet.Id, 30m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        await service.MarkSentAsync(claim.Id, BusinessClock.Today, "By hand", null, shop.Catalog.Admin.Id, UserRole.Owner);
        ExpiryClaimLine tablet = await database.Context.ExpiryClaimLines.AsNoTracking().SingleAsync(line => line.BatchNo == "TAB-NEAR");
        (int Count, decimal Sum) before = (await database.Context.StockMovements.CountAsync(), await database.Context.StockMovements.SumAsync(movement => movement.QuantityChange));
        decimal payable = await PayableAsync(database, shop.Supplier.Id);

        await service.RejectLineAsync(tablet.Id, 30m, "Below minimum shelf life on receipt", RejectedGoodsAction.WriteOff, shop.Catalog.Admin.Id, UserRole.Owner);

        Assert.Equal(before, (await database.Context.StockMovements.CountAsync(), await database.Context.StockMovements.SumAsync(movement => movement.QuantityChange)));
        Assert.Equal(0m, await OnHandAsync(database.Context, shop.NearTablet.Id));
        Assert.Equal(payable, await PayableAsync(database, shop.Supplier.Id));
        ExpiryClaimLine rejected = await database.Context.ExpiryClaimLines.AsNoTracking().SingleAsync(line => line.Id == tablet.Id);
        Assert.Equal((ExpiryClaimLineState.RejectedWrittenOff, 30m, 604.80m, "Master Admin"), (rejected.State, rejected.WrittenOffQuantity, rejected.RejectedValue, rejected.WrittenOffByName));
        Assert.NotNull(rejected.WrittenOffAtUtc);
        Assert.Empty(await service.GetHeldStockAsync());
        Assert.Equal(1120m, Assert.Single(await service.GetClaimsAsync(open: true)).Balance);
    }

    [Fact]
    public async Task ARejectedLineTakenBack_IsHeldNotSellable_UntilReleased()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        ExpiryClaim claim = await service.CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 20m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        await service.MarkSentAsync(claim.Id, BusinessClock.Today, "By hand", null, shop.Catalog.Admin.Id, UserRole.Owner);
        Guid line = (await database.Context.ExpiryClaimLines.SingleAsync()).Id;

        await service.RejectLineAsync(line, 20m, "Not our batch", RejectedGoodsAction.ReturnToHeldStock, shop.Catalog.Admin.Id, UserRole.Owner);
        Assert.Equal(0m, await OnHandAsync(database.Context, shop.Near.Id));
        Assert.Equal(20m, Assert.Single(await service.GetHeldStockAsync()).Quantity);
        Assert.Equal(ExpiryClaimStatus.Rejected, Assert.Single(await service.GetClaimsAsync(open: false)).Status);

        await service.ReleaseHeldRejectedAsync(line, shop.Catalog.Admin.Id, UserRole.Owner);
        Assert.Equal(20m, await OnHandAsync(database.Context, shop.Near.Id));
        Assert.Empty(await service.GetHeldStockAsync());
    }

    [Fact]
    public async Task EditingTheProductAfterSaving_ChangesNothingOnTheClaimOrItsReports()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        ExpiryClaim claim = await service.CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 20m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        await service.MarkSentAsync(claim.Id, BusinessClock.Today, "By hand", null, shop.Catalog.Admin.Id, UserRole.Owner);
        string Describe((ExpiryClaim Claim, IReadOnlyList<ExpiryClaimLine> Lines, IReadOnlyList<ExpiryClaimCredit> Credits) read) =>
            string.Join("|", read.Lines.Select(line => $"{line.ItemName};{line.Pack};{line.HsnCode};{line.GstRate};{line.PurchaseRate};{line.TaxableValue};{line.CgstAmount};{line.LineValue}"))
            + $"|{read.Claim.SupplierName};{read.Claim.SupplierGstin};{read.Claim.SupplierAddress};{read.Claim.TotalValue}";
        string before = Describe(await service.GetClaimAsync(claim.Id));
        string ageingBefore = Text(await service.PendingAgeingAsync());
        string recoveryBefore = Text(await service.RecoveryAsync(BusinessClock.Today.AddDays(-30), BusinessClock.Today));

        Drug drug = await database.Context.Drugs.SingleAsync(item => item.Id == shop.Syrup.Id);
        drug.Name = "Renamed Syrup";
        drug.HsnCode = "99999999";
        drug.GstRate = 18m;
        drug.Unit = "200 ML";
        Supplier supplier = await database.Context.Suppliers.SingleAsync(item => item.Id == shop.Supplier.Id);
        supplier.Address = "Moved";
        supplier.Gstin = "07AAAAA9999A1Z5";
        await database.Context.SaveChangesAsync();

        Assert.Equal(before, Describe(await service.GetClaimAsync(claim.Id)));
        Assert.Equal(ageingBefore, Text(await service.PendingAgeingAsync()));
        Assert.Equal(recoveryBefore, Text(await service.RecoveryAsync(BusinessClock.Today.AddDays(-30), BusinessClock.Today)));

        static string Text(ReportTable table) => string.Join("|", table.Columns) + "\n" + string.Join("\n", table.Rows.Select(row => string.Join("|", row)));
    }

    [Fact]
    public async Task VoidingADraft_ReturnsTheStockExactly()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        Dictionary<Guid, decimal> locations = await database.Context.StockLocationBalances.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Quantity);
        decimal near = await OnHandAsync(database.Context, shop.Near.Id), tablet = await OnHandAsync(database.Context, shop.NearTablet.Id);

        ExpiryClaim claim = await service.CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 7m), new(shop.NearTablet.Id, 30m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        await service.VoidDraftAsync(claim.Id, "Built for the wrong supplier", shop.Catalog.Admin.Id, UserRole.Owner);

        Assert.Equal((near, tablet), (await OnHandAsync(database.Context, shop.Near.Id), await OnHandAsync(database.Context, shop.NearTablet.Id)));
        Assert.Equal(locations, await database.Context.StockLocationBalances.AsNoTracking().ToDictionaryAsync(item => item.Id, item => item.Quantity));
        Assert.Equal(near, (await database.Context.Batches.AsNoTracking().SingleAsync(item => item.Id == shop.Near.Id)).Quantity);
        Assert.Empty(await service.GetHeldStockAsync());
        ExpiryClaim voided = await database.Context.ExpiryClaims.AsNoTracking().SingleAsync();
        Assert.Equal((ExpiryClaimStatus.Voided, "Built for the wrong supplier", "Master Admin"), (voided.Status, voided.VoidReason, voided.VoidedByName));
        Assert.Contains(await database.Context.AuditLogs.ToListAsync(), log => log.Action == "ExpiryClaimVoided" && log.EntityId == claim.Id);
        // The batches are claimable again.
        Assert.Contains(await service.GetClaimableAsync(), row => row.BatchId == shop.Near.Id && row.Available == near);
    }

    [Fact]
    public async Task TwoSessions_CannotClaimTheSameQuantity()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        await using PharmaBillDbContext first = database.OpenContext();
        await using PharmaBillDbContext second = database.OpenContext();
        using var start = new ManualResetEventSlim();
        Task<ExpiryClaim> Claim(PharmaBillDbContext context) => Task.Run(async () =>
        {
            start.Wait();
            return await Service(database, context).CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 20m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        });
        Task<ExpiryClaim>[] racing = [Claim(first), Claim(second)];
        start.Set();
        await Task.WhenAll(racing.Select(task => task.ContinueWith(_ => { })));

        Assert.Single(racing, task => task.IsCompletedSuccessfully);
        Assert.Single(racing, task => task.IsFaulted);
        database.Context.ChangeTracker.Clear();
        Assert.Equal(20m, await database.Context.ExpiryClaimLines.SumAsync(line => line.Quantity));
        Assert.Equal(0m, await OnHandAsync(database.Context, shop.Near.Id));
    }

    [Fact]
    public async Task AnInterStateSupplier_IsClaimedWithIgst()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database, supplierGstin: "07AAAAA9999A1ZM");
        ExpiryClaim claim = await Service(database).CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 10m)], null, shop.Catalog.Admin.Id, UserRole.Owner);
        Assert.Equal((true, "07", 0m, 60m, 560m), (claim.IsInterState, claim.SupplierStateCode, claim.CgstAmount, claim.IgstAmount, claim.TotalValue));
    }

    [Fact]
    public async Task OutsideTheWindow_OrFromAnotherSupplier_IsRefused()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        InvalidOperationException far = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDraftAsync(shop.Supplier.Id, [new(shop.Far.Id, 1m)], null, shop.Catalog.Admin.Id, UserRole.Owner));
        Assert.Contains("outside", far.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateDraftAsync(shop.Catalog.Suppliers[1].Id, [new(shop.Near.Id, 1m)], null, shop.Catalog.Admin.Id, UserRole.Owner));
        Assert.Empty(await database.Context.ExpiryClaims.ToListAsync());
    }

    [Fact]
    public async Task ASupplierWithoutItsOwnWindow_FollowsTheDefault()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SetUpAsync(database);
        ExpiryReturnService service = Service(database);
        await service.SaveSupplierPolicyAsync(shop.Supplier.Id, null, null, null, shop.Catalog.Admin.Id);
        await service.SaveDefaultWindowAsync(5, 0, shop.Catalog.Admin.Id);
        // 30 days to expiry is outside a 5-day default; 10 days is outside too.
        Assert.DoesNotContain(await service.GetClaimableAsync(), row => row.SupplierId == shop.Supplier.Id);
        await service.SaveDefaultWindowAsync(200, 0, shop.Catalog.Admin.Id);
        Assert.Equal(3, (await service.GetClaimableAsync()).Count(row => row.SupplierId == shop.Supplier.Id));
    }

    [Fact]
    public async Task AFreshInstall_CreatesAndSavesAClaim()
    {
        await using var database = await DatabaseTestContext.CreateAsync(createSchema: false);
        await database.CreateDbInitializer().InitializeAsync();
        Shop shop = await SetUpAsync(database);
        Assert.Equal((90, 30), ((await database.Context.PharmacyProfiles.SingleAsync()).ExpiryReturnDaysBefore, (await database.Context.PharmacyProfiles.SingleAsync()).ExpiryReturnDaysAfter));
        ExpiryClaim claim = await Service(database).CreateDraftAsync(shop.Supplier.Id, [new(shop.Near.Id, 20m)], "Monthly expiry return", shop.Catalog.Admin.Id, UserRole.Owner);
        database.Context.ChangeTracker.Clear();
        ExpiryClaim saved = await database.Context.ExpiryClaims.SingleAsync();
        Assert.Equal((claim.ClaimNo, 1120m, ExpiryClaimStatus.Draft, 90, "Expiry accepted 3 months before to 1 month after, at purchase rate."), (saved.ClaimNo, saved.TotalValue, saved.Status, saved.PolicyDaysBeforeExpiry, saved.PolicyNote));
    }

    private static async Task<decimal> PayableAsync(DatabaseTestContext database, Guid supplierId)
    {
        List<SupplierLedgerEntry> entries = await database.Context.SupplierLedgerEntries.AsNoTracking().Where(entry => entry.SupplierId == supplierId).ToListAsync();
        return entries.Sum(entry => entry.Credit - entry.Debit);
    }
}
