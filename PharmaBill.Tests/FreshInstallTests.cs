using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

/// <summary>
/// End to end as a new pharmacy installs it: schema comes only from migrations (via the real startup
/// initializer), never from the EF model, then a bill is saved.
/// </summary>
public sealed class FreshInstallTests
{
    private sealed class AllowAll : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);

        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    [Fact]
    public async Task FreshInstall_FromMigrationsOnly_SavesARetailBill()
    {
        await using var database = await DatabaseTestContext.CreateAsync(createSchema: false);
        await database.CreateDbInitializer().InitializeAsync();
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);

        RetailSaleResult result = await database.CreateRetailBillingService(new AllowAll()).SaveSaleAsync(
            new SaveRetailSaleInput(
                "Fresh Install Patient",
                "9111100099",
                "Lal Chowk",
                null,
                null,
                null,
                [new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 2m, 0m, 30m)],
                [new RetailPaymentInput("Cash", 60m)],
                false),
            catalog.Admin.Id,
            UserRole.Pharmacist);

        Sale saved = await database.Context.Sales.AsNoTracking().SingleAsync(sale => sale.Id == result.Sale.Id);
        Assert.Equal(60m, saved.TotalAmount);
        Assert.True(saved.IsLocked);
    }
}
