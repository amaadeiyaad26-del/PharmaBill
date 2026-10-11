using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class InventoryPurchaseTests
{
    [Fact]
    public async Task PurchaseReceipt_CreatesBatchAndMatchingStockMovement()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var supplier = new Supplier { Name = "Supplier" };
        var drug = new Drug { Name = "Test medicine" };
        database.Context.AddRange(supplier, drug);
        await database.Context.SaveChangesAsync();
        var service = database.CreatePurchaseService(new AllowAllEntitlements());

        var invoice = await service.SavePurchaseAsync(
            new SavePurchaseInput(
                supplier.Id,
                "SUP-INV-1",
                DateOnly.FromDateTime(DateTime.Today),
                30m,
                0m,
                1.5m,
                31.5m,
                [
                    new PurchaseLineInput(
                        drug.Id, "B-1", DateOnly.FromDateTime(DateTime.Today.AddMonths(4)),
                        2m, 1m, 15m, 10m, 5m, 0m, 20m, "A1"),
                    new PurchaseLineInput(
                        drug.Id, "B-1", DateOnly.FromDateTime(DateTime.Today.AddMonths(4)),
                        1m, 0m, 15m, 10m, 5m, 0m, 10m, "A1")
                ]),
            Guid.NewGuid(),
            UserRole.Owner);

        var batch = await database.Context.Batches.SingleAsync();
        var movementTotal = await database.Context.StockMovements
            .Where(movement => movement.BatchId == batch.Id)
            .SumAsync(movement => movement.QuantityChange);

        Assert.Equal("SUP-INV-1", invoice.InvoiceNo);
        Assert.Equal(4m, batch.Quantity);
        Assert.Equal(batch.Quantity, movementTotal);
        Assert.Equal(31.5m, invoice.TotalAmount);
        Assert.Single(await database.Context.Batches.ToListAsync());
        Assert.Contains(await database.Context.SupplierLedgerEntries.ToListAsync(),
            entry => entry.SupplierId == supplier.Id && entry.Credit == 31.5m && entry.Debit == 0m
                && (entry.EntryType == "PurchaseBill" || entry.EntryType == "PurchaseInvoice"));
    }

    [Fact]
    public async Task PurchaseReceipt_RejectsExpiredBatch()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var supplier = new Supplier { Name = "Supplier" };
        var drug = new Drug { Name = "Test medicine" };
        database.Context.AddRange(supplier, drug);
        await database.Context.SaveChangesAsync();
        var service = database.CreatePurchaseService(new AllowAllEntitlements());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SavePurchaseAsync(
            new SavePurchaseInput(
                supplier.Id, "SUP-INV-2", DateOnly.FromDateTime(DateTime.Today),
                10m, 0m, 0m, 10m,
                [
                    new PurchaseLineInput(
                        drug.Id, "EXPIRED", DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
                        1m, 0m, 15m, 10m, 0m, 0m, 10m, null)
                ]),
            Guid.NewGuid(),
            UserRole.Owner));

        Assert.Empty(await database.Context.Batches.ToListAsync());
        Assert.Empty(await database.Context.PurchaseInvoices.ToListAsync());
    }

    [Fact]
    public async Task PurchaseReturn_RejectsQuantityAboveMovementBasedStock()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var supplier = new Supplier { Name = "Supplier" };
        var drug = new Drug { Name = "Test medicine" };
        var batch = new Batch
        {
            DrugId = drug.Id,
            SupplierId = supplier.Id,
            BatchNo = "B-1",
            Quantity = 2m,
            PurchasePrice = 10m
        };
        database.Context.AddRange(supplier, drug, batch);
        database.Context.StockMovements.Add(new StockMovement
        {
            BatchId = batch.Id,
            DrugId = drug.Id,
            QuantityChange = 2m,
            MovementType = "PurchaseReceipt"
        });
        await database.Context.SaveChangesAsync();
        var service = database.CreatePurchaseService(new AllowAllEntitlements());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SavePurchaseReturnAsync(
            supplier.Id,
            "RET-1",
            DateOnly.FromDateTime(DateTime.Today),
            [new PurchaseReturnLineInput(batch.Id, 3m)],
            "Damaged",
            Guid.NewGuid(),
            UserRole.Owner));

        Assert.Empty(await database.Context.PurchaseReturns.ToListAsync());
        Assert.Equal(2m, (await database.Context.Batches.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task PurchaseReturn_DecreasesBatchAndCreatesOutgoingRegisterEntry()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var supplier = new Supplier { Name = "Supplier" };
        var drug = new Drug { Name = "Controlled medicine", Schedule = "H1" };
        var batch = new Batch
        {
            DrugId = drug.Id,
            SupplierId = supplier.Id,
            BatchNo = "H1-B1",
            Quantity = 5m,
            PurchasePrice = 10m
        };
        database.Context.AddRange(supplier, drug, batch);
        database.Context.StockMovements.Add(new StockMovement
        {
            BatchId = batch.Id,
            DrugId = drug.Id,
            QuantityChange = 5m,
            MovementType = "PurchaseReceipt"
        });
        await database.Context.SaveChangesAsync();
        var service = database.CreatePurchaseService(new AllowAllEntitlements());

        await service.SavePurchaseReturnAsync(
            supplier.Id,
            "RET-2",
            DateOnly.FromDateTime(DateTime.Today),
            [new PurchaseReturnLineInput(batch.Id, 2m)],
            "Damaged packaging",
            Guid.NewGuid(),
            UserRole.Owner);

        database.Context.ChangeTracker.Clear();
        batch = await database.Context.Batches.SingleAsync();
        Assert.Equal(3m, batch.Quantity);
        Assert.Equal(3m, await database.Context.StockMovements
            .Where(movement => movement.BatchId == batch.Id)
            .SumAsync(movement => movement.QuantityChange));
        Assert.Contains(await database.Context.ScheduleRegisterEntries.ToListAsync(),
            entry => entry.RegisterType == "H1" && entry.Quantity == 2m);
    }

    [Fact]
    public async Task StockVerification_PostingWritesMovementAndUpdatesCachedQuantity()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "Verified medicine" };
        var batch = new Batch { DrugId = drug.Id, BatchNo = "V-1", Quantity = 4m };
        database.Context.AddRange(drug, batch);
        database.Context.StockMovements.Add(new StockMovement
        {
            BatchId = batch.Id,
            DrugId = drug.Id,
            QuantityChange = 4m,
            MovementType = "PurchaseReceipt"
        });
        await database.Context.SaveChangesAsync();
        var service = new StockVerificationService(
            new UnitOfWork(database.Context),
            new AllowAllEntitlements());
        var session = await service.StartAsync("COUNT-1", Guid.NewGuid(), UserRole.Owner);
        await service.SetCountedQuantityAsync(session.Id, batch.Id, 3m);
        await service.PostAsync(session.Id, Guid.NewGuid(), UserRole.Owner);

        database.Context.ChangeTracker.Clear();
        batch = await database.Context.Batches.SingleAsync();
        Assert.Equal(3m, batch.Quantity);
        Assert.Equal(3m, await database.Context.StockMovements
            .Where(movement => movement.BatchId == batch.Id)
            .SumAsync(movement => movement.QuantityChange));
    }

    private sealed class AllowAllEntitlements : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(EntitlementStatus.TRIAL);

        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<bool> CanPerformAsync(
            ProtectedOperation operation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
