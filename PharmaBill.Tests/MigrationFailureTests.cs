using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Persistence;
using Xunit;

namespace PharmaBill.Tests;

public sealed class MigrationFailureTests
{
    [Fact]
    public async Task FailedMigration_StopsStartupAndNamesTheMigration()
    {
        await using var database = await DatabaseTestContext.CreateAsync(createSchema: false);
        await database.Context.Database.MigrateAsync();
        string latest = (await database.Context.Database.GetAppliedMigrationsAsync()).Last();
        // Simulate a history that lost track of the latest migration while what it creates already exists:
        // re-applying it must fail, and startup must refuse rather than patch around it.
        await database.Context.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = {0}", latest);

        var error = await Assert.ThrowsAsync<DatabaseMigrationException>(() => database.CreateDbInitializer().InitializeAsync());

        Assert.Equal(latest, error.MigrationId);
        Assert.Contains("Database upgrade failed at migration '" + latest + "'", error.Message);
        // SQLite's own reason is passed on. The latest migration (ExpiryReturnClaims) adds columns, the first being
        // Suppliers.ExpiryReturnDaysAfter; one that creates a table reports "already exists" instead.
        Assert.Contains("duplicate column name: ExpiryReturnDaysAfter", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(latest, await database.Context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task FreshDatabase_IsCreatedByMigrationsWithNothingPending()
    {
        await using var database = await DatabaseTestContext.CreateAsync(createSchema: false);

        await database.CreateDbInitializer().InitializeAsync();

        Assert.Empty(await database.Context.Database.GetPendingMigrationsAsync());
        Assert.Empty(await database.Context.PharmacyProfiles.ToListAsync());
    }

    [Fact]
    public void DebugBuilds_UseTheirOwnDataFolder()
    {
#if DEBUG
        Assert.Equal("PharmaBill-Debug", AppDataPaths.RootFolderName);
#else
        Assert.Equal("PharmaBill", AppDataPaths.RootFolderName);
#endif
        // The default location (the test run itself is redirected to a temp folder).
        Assert.EndsWith(AppDataPaths.RootFolderName, AppDataPaths.LocalRootDirectory);
    }
}
