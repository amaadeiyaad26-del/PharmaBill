using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class AddStockTests
{
    private sealed class Entitlements(bool readOnly) : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(readOnly ? EntitlementStatus.EXPIRED : EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(readOnly);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) =>
            Task.FromResult(!readOnly);
    }

    private static AddStockService Create(DatabaseTestContext database, bool readOnly = false)
    {
        var uow = new UnitOfWork(database.Context);
        var ent = new Entitlements(readOnly);
        return new AddStockService(uow, database.CreatePurchaseService(ent, uow), ent, new NullLicenseRuntimeGuard());
    }

    private static AddStockInput Input(Guid supplierId, string schedule = "H", string invoice = "INV-1", string batch = "B1", decimal qty = 10m) =>
        new(null, null, "Alprazolam 0.5", "Alprazolam", "Acme", schedule, batch,
            DateOnly.FromDateTime(DateTime.Today.AddYears(1)), 30m, 20m, qty, 0m, 10m, 12m,
            supplierId, invoice, DateOnly.FromDateTime(DateTime.Today), "R1");

    [Fact]
    public async Task AddStock_creates_drug_batch_movement_and_audit_in_one_save()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var supplier = await Create(database).AddSupplierAsync("Sup", Guid.NewGuid(), UserRole.Owner);

        var result = await Create(database).AddStockAsync(Input(supplier.Id), Guid.NewGuid(), UserRole.Owner);

        Assert.Equal(10m, result.TotalStock);
        Assert.Single(await database.Context.Drugs.ToListAsync());
        var batch = await database.Context.Batches.SingleAsync();
        Assert.Equal(10m, await database.Context.StockMovements.Where(m => m.BatchId == batch.Id).SumAsync(m => m.QuantityChange));
        Assert.Single(await database.Context.PurchaseInvoices.ToListAsync());
        Assert.NotEmpty(await database.Context.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AddStock_same_batch_merges_into_existing_batch()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var supplier = await Create(database).AddSupplierAsync("Sup", Guid.NewGuid(), UserRole.Owner);
        var first = await Create(database).AddStockAsync(Input(supplier.Id), Guid.NewGuid(), UserRole.Owner);

        var second = await Create(database).AddStockAsync(
            Input(supplier.Id, invoice: "INV-2", qty: 5m) with { DrugId = first.DrugId }, Guid.NewGuid(), UserRole.Owner);

        Assert.True(second.MergedIntoExistingBatch);
        Assert.Equal(15m, second.TotalStock);
        Assert.Single(await database.Context.Batches.ToListAsync());
    }

    [Fact]
    public async Task AddStock_is_blocked_in_read_only_mode()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var supplier = await Create(database).AddSupplierAsync("Sup", Guid.NewGuid(), UserRole.Owner);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Create(database, readOnly: true).AddStockAsync(Input(supplier.Id), Guid.NewGuid(), UserRole.Owner));
        Assert.Empty(await database.Context.Batches.ToListAsync());
    }

    [Fact]
    public async Task AddStock_rejects_expired_expiry_and_missing_schedule()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var supplier = await Create(database).AddSupplierAsync("Sup", Guid.NewGuid(), UserRole.Owner);

        await Assert.ThrowsAnyAsync<Exception>(() => Create(database).AddStockAsync(
            Input(supplier.Id) with { ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)) },
            Guid.NewGuid(), UserRole.Owner));
        await Assert.ThrowsAnyAsync<Exception>(() => Create(database).AddStockAsync(
            Input(supplier.Id, schedule: ""), Guid.NewGuid(), UserRole.Owner));
    }

    [Fact]
    public async Task WriteOffAllExpired_only_touches_expired_batches_with_stock()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "Old medicine" };
        database.Context.Drugs.Add(drug);
        await database.Context.SaveChangesAsync();
        var expired = new Batch { DrugId = drug.Id, BatchNo = "E1", ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-5)), Quantity = 4m };
        var fresh = new Batch { DrugId = drug.Id, BatchNo = "F1", ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)), Quantity = 6m };
        database.Context.Batches.AddRange(expired, fresh);
        database.Context.StockMovements.AddRange(
            new StockMovement { BatchId = expired.Id, DrugId = drug.Id, QuantityChange = 4m, MovementType = "Purchase" },
            new StockMovement { BatchId = fresh.Id, DrugId = drug.Id, QuantityChange = 6m, MovementType = "Purchase" });
        await database.Context.SaveChangesAsync();
        var inventory = new InventoryService(database.Context, new UnitOfWork(database.Context), new Entitlements(false));

        var count = await inventory.WriteOffAllExpiredAsync("Expired stock", Guid.NewGuid(), UserRole.Owner);

        Assert.Equal(1, count);
        Assert.Equal(0m, await database.Context.StockMovements.Where(m => m.BatchId == expired.Id).SumAsync(m => m.QuantityChange));
        Assert.Equal(6m, await database.Context.StockMovements.Where(m => m.BatchId == fresh.Id).SumAsync(m => m.QuantityChange));
    }
}
