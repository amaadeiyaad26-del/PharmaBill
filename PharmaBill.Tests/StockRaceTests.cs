using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Another writer (a phone sync, an import, a second counter) takes stock after a bill's early check
// but before its transaction starts. The bill must be refused, never sold into negative stock.
public sealed class StockRaceTests
{
    [Fact]
    public async Task RetailSale_RefusesStockTakenAfterTheEarlyCheck()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (drug, batch, location) = await SeedAsync(database, stock: 5m);
        var race = new CompetingWriteBeforeTransaction(() => TakeStockAsync(database, drug, batch, location, 4m));
        await using PharmaBillDbContext context = OpenContext(database, race);
        var unitOfWork = new UnitOfWork(context);
        var branches = new BranchService(unitOfWork, database.BranchSettings, database.Storage);
        var billing = new RetailBillingService(unitOfWork, new NumberSeriesService(unitOfWork), new Allowed(),
            new CatalogSearchService(context, branches), new StorageLocationService(context), branches, new NullLicenseRuntimeGuard());

        race.Armed = true;
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => billing.SaveSaleAsync(
            new SaveRetailSaleInput("Walk-in", "9876543210", null, null, null, null,
                [new RetailSaleLineInput(drug.Id, batch.Id, 3m, 0m, 50m)], [new RetailPaymentInput("Cash", 150m)], false),
            Guid.NewGuid(), UserRole.Owner));

        Assert.True(race.Fired);
        Assert.Contains("(1 available)", refused.Message);
        Assert.Equal(0, await database.Context.Sales.CountAsync());
        Assert.Equal(1m, await OnHandAsync(database, batch));
    }

    [Fact]
    public async Task StockAdjustment_RefusesToGoBelowZeroAfterAConcurrentSale()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (drug, batch, location) = await SeedAsync(database, stock: 5m);
        var race = new CompetingWriteBeforeTransaction(() => TakeStockAsync(database, drug, batch, location, 4m));
        await using PharmaBillDbContext context = OpenContext(database, race);
        var inventory = new InventoryService(context, new UnitOfWork(context), new Allowed());

        race.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inventory.AdjustStockAsync(batch.Id, -3m, "Damaged strip", Guid.NewGuid(), UserRole.Owner));

        Assert.True(race.Fired);
        Assert.Equal(1m, await OnHandAsync(database, batch));
    }

    [Fact]
    public async Task RetailSale_StillSellsWhenEnoughIsLeft_AndBatchQuantityIsFresh()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var (drug, batch, location) = await SeedAsync(database, stock: 10m);
        var race = new CompetingWriteBeforeTransaction(() => TakeStockAsync(database, drug, batch, location, 4m));
        await using PharmaBillDbContext context = OpenContext(database, race);
        var unitOfWork = new UnitOfWork(context);
        var branches = new BranchService(unitOfWork, database.BranchSettings, database.Storage);
        var billing = new RetailBillingService(unitOfWork, new NumberSeriesService(unitOfWork), new Allowed(),
            new CatalogSearchService(context, branches), new StorageLocationService(context), branches, new NullLicenseRuntimeGuard());

        race.Armed = true;
        await billing.SaveSaleAsync(
            new SaveRetailSaleInput("Walk-in", "9876543210", null, null, null, null,
                [new RetailSaleLineInput(drug.Id, batch.Id, 3m, 0m, 50m)], [new RetailPaymentInput("Cash", 150m)], false),
            Guid.NewGuid(), UserRole.Owner);

        Assert.Equal(3m, await OnHandAsync(database, batch));
        Assert.Equal(3m, (await context.Batches.AsNoTracking().SingleAsync(b => b.Id == batch.Id)).Quantity);
    }

    private static async Task<(Drug Drug, Batch Batch, StorageLocation Location)> SeedAsync(DatabaseTestContext database, decimal stock)
    {
        database.Context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Race Pharmacy", BusinessMode = BusinessMode.Retail, InvoicePrefix = "WIN1" });
        var drug = new Drug { Name = "Race medicine", GstRate = 12m, HsnCode = "3004" };
        var batch = new Batch { DrugId = drug.Id, BatchNo = "RACE-1", ExpiryDate = BusinessClock.Today.AddMonths(6), Quantity = stock, Mrp = 100m, SalePrice = 95m };
        database.Context.AddRange(drug, batch);
        await database.Context.SaveChangesAsync();
        StorageLocation location = await database.CreateStorageLocations().GetDefaultRetailLocationAsync();
        database.Context.StockMovements.Add(new StockMovement { BatchId = batch.Id, DrugId = drug.Id, LocationId = location.Id, QuantityChange = stock, MovementType = "PurchaseReceipt" });
        await StorageLocationService.ApplyBalanceDeltaAsync(database.Context, location.Id, batch.Id, drug.Id, stock);
        await database.Context.SaveChangesAsync();
        return (drug, batch, location);
    }

    // Commits on the shared connection, just before the bill's own transaction begins.
    private static async Task TakeStockAsync(DatabaseTestContext database, Drug drug, Batch batch, StorageLocation location, decimal quantity)
    {
        await using PharmaBillDbContext other = OpenContext(database, null);
        other.StockMovements.Add(new StockMovement { BatchId = batch.Id, DrugId = drug.Id, LocationId = location.Id, QuantityChange = -quantity, MovementType = "RetailSale", Notes = "Phone sale" });
        await StorageLocationService.ApplyBalanceDeltaAsync(other, location.Id, batch.Id, drug.Id, -quantity);
        await other.SaveChangesAsync();
    }

    private static Task<decimal> OnHandAsync(DatabaseTestContext database, Batch batch) =>
        database.Context.StockMovements.Where(m => m.BatchId == batch.Id).SumAsync(m => m.QuantityChange);

    private static PharmaBillDbContext OpenContext(DatabaseTestContext database, IInterceptor? race)
    {
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        var builder = new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite(database.Connection)
            .AddInterceptors(new SaveChangesAuditInterceptor(deviceId));
        if (race != null)
        {
            builder.AddInterceptors(race);
        }
        return new PharmaBillDbContext(builder.Options, deviceId);
    }

    private sealed class CompetingWriteBeforeTransaction(Func<Task> write) : DbTransactionInterceptor
    {
        public bool Armed { get; set; }

        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                await write();
                Fired = true;
            }
            return result;
        }
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
