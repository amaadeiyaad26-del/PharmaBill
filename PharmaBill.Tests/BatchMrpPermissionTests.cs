using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// A batch's MRP caps every later sale price, so changing it needs stock-entry permission from every screen and terminal.
public sealed class BatchMrpPermissionTests
{
    [Theory]
    [InlineData(UserRole.BillingClerk, false)]
    [InlineData(UserRole.Salesman, false)]
    [InlineData(UserRole.Accountant, false)]
    [InlineData(UserRole.Pharmacist, true)]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Owner, true)]
    public async Task OnlyStockRolesCanChangeABatchMrp(UserRole role, bool allowed)
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var drug = new Drug { Name = "MRP medicine", HsnCode = "3004", GstRate = 12m };
        var batch = new Batch { DrugId = drug.Id, BatchNo = "MRP-1", Mrp = 30m, ExpiryDate = BusinessClock.Today.AddYears(1) };
        database.Context.AddRange(drug, batch);
        await database.Context.SaveChangesAsync();
        var inventory = new InventoryService(database.Context, database.CreateUnitOfWork(), new Allowed());

        if (allowed)
        {
            await inventory.UpdateBatchMrpAsync(batch.Id, 32m, role);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inventory.UpdateBatchMrpAsync(batch.Id, 32m, role));
        }

        Assert.Equal(allowed ? 32m : 30m, (await database.Context.Batches.AsNoTracking().SingleAsync(b => b.Id == batch.Id)).Mrp);
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
