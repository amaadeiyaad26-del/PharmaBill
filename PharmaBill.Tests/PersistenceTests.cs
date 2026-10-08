using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Metadata;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task SaveChanges_SoftDeletesAndCreatesChangeLogRows()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var profile = new PharmacyProfile { Name = "Test Pharmacy" };
        database.Context.PharmacyProfiles.Add(profile);
        await database.Context.SaveChangesAsync();

        database.Context.PharmacyProfiles.Remove(profile);
        await database.Context.SaveChangesAsync();

        Assert.Empty(await database.Context.PharmacyProfiles.ToListAsync());
        var stored = await database.Context.PharmacyProfiles
            .IgnoreQueryFilters()
            .SingleAsync();
        Assert.True(stored.IsDeleted);
        Assert.Equal(database.Context.DeviceId, stored.DeviceId);
        Assert.NotEmpty(stored.HlcStamp);

        var logs = await database.Context.ChangeLogs
            .Where(log => log.EntityId == profile.Id)
            .OrderBy(log => log.ChangedAtUtc)
            .ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal("Added", logs[0].Operation);
        Assert.Equal("SoftDeleted", logs[1].Operation);
    }

    [Fact]
    public async Task NumberSeries_AllocatesIncreasingNumbersWithinFinancialYear()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var unitOfWork = new UnitOfWork(database.Context);
        var numberSeries = new NumberSeriesService(unitOfWork);

        await Assert.ThrowsAsync<InvalidOperationException>(() => numberSeries.AllocateAsync());

        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            Assert.Equal("WIN1/2026-27/000001", await numberSeries.AllocateAsync(
                date: new DateOnly(2026, 4, 1)));
            Assert.Equal("WIN1/2026-27/000002", await numberSeries.AllocateAsync(
                date: new DateOnly(2027, 3, 31)));
            await transaction.CommitAsync();
        }

        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            Assert.Equal("WIN1/2025-26/000001", await numberSeries.AllocateAsync(
                date: new DateOnly(2026, 3, 31)));
            await transaction.CommitAsync();
        }
    }

    [Fact]
    public async Task NumberSeries_RollbackDoesNotConsumeANumber()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var unitOfWork = new UnitOfWork(database.Context);
        var numberSeries = new NumberSeriesService(unitOfWork);
        string rolledBackNumber;

        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            rolledBackNumber = await numberSeries.AllocateAsync(date: new DateOnly(2026, 4, 1));
            await transaction.RollbackAsync();
        }

        database.Context.ChangeTracker.Clear();
        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            var allocatedNumber = await numberSeries.AllocateAsync(date: new DateOnly(2026, 4, 1));
            Assert.Equal(rolledBackNumber, allocatedNumber);
            await transaction.CommitAsync();
        }
    }

    [Fact]
    public async Task DbInitializer_AppliesInitialMigration()
    {
        await using var database = await DatabaseTestContext.CreateAsync(createSchema: false);

        await database.CreateDbInitializer().InitializeAsync();

        Assert.EndsWith(
            "_BranchIdColumns",
            (await database.Context.Database.GetAppliedMigrationsAsync()).Last());
        Assert.Equal(49, database.Context.Model.GetEntityTypes().Count());

        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = database.Connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"AuditLogs\");";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }
        }

        Assert.Contains("BranchId", columns);

        database.Context.AuditLogs.Add(new AuditLog
        {
            Action = "SchemaSmokeTest",
            EntityName = "AuditLogs",
            BranchId = null,
        });
        await database.Context.SaveChangesAsync();
    }

    [Fact]
    public async Task MigrationSnapshot_MatchesCurrentModel()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var snapshot = (ModelSnapshot)Activator.CreateInstance(
            typeof(PharmaBillDbContext).Assembly.GetTypes()
                .Single(type => type.Name == "PharmaBillDbContextModelSnapshot"),
            nonPublic: true)!;
        var snapshotModel = database.Context.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshot.Model, designTime: true);
        var differ = database.Context.GetService<IMigrationsModelDiffer>();
        var currentModel = database.Context.GetService<IDesignTimeModel>().Model;
        var differences = differ.GetDifferences(
            snapshotModel.GetRelationalModel(),
            currentModel.GetRelationalModel());
        Assert.True(differences.Count == 0,
            string.Join(Environment.NewLine, differences.Select(operation =>
                operation switch
                {
                    Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation drop =>
                        $"Drop {drop.Table}.{drop.Name}",
                    Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation add =>
                        $"Add {add.Table}.{add.Name} ({add.ClrType.Name})",
                    _ => operation.GetType().Name
                })));
    }

    [Fact]
    public async Task FirstRunSecurityMigration_PreservesExistingBusinessMode()
    {
        await using var database = await DatabaseTestContext.CreateAsync(createSchema: false);
        var firstMigration = database.Context.Database.GetMigrations().First();
        await database.Context.Database.MigrateAsync(firstMigration);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await database.Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO PharmacyProfiles
                (Id, Name, BusinessMode, CreatedAtUtc, UpdatedAtUtc, DeviceId, IsDeleted, HlcStamp, SyncState)
            VALUES
                ({id}, {"Legacy pharmacy"}, {"BOTH"}, {now}, {now}, {Guid.NewGuid()}, {false}, {"legacy"}, {0})
            """);

        await database.CreateDbInitializer().InitializeAsync();
        database.Context.ChangeTracker.Clear();

        var profile = await database.Context.PharmacyProfiles.SingleAsync();
        Assert.Equal(BusinessMode.Both, profile.BusinessMode);
        Assert.Equal("WIN1", profile.InvoicePrefix);
    }

    [Fact]
    public async Task EncryptedDatabase_CannotBeReadWithoutItsKey()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Encrypted" });
        await database.Context.SaveChangesAsync();
        var databasePath = database.Connection.DataSource;
        await database.Context.DisposeAsync();
        await database.Connection.CloseAsync();

        await using var unkeyedConnection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await unkeyedConnection.OpenAsync();
        await using var command = unkeyedConnection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PharmacyProfiles";
        await Assert.ThrowsAnyAsync<SqliteException>(() => command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task BusinessModeService_RequiresLicencesAndAuditsApprovedChanges()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var profile = new PharmacyProfile { Name = "Mode test", BusinessMode = BusinessMode.Retail };
        database.Context.PharmacyProfiles.Add(profile);
        var owner = new AppUser
        {
            DisplayName = "Owner",
            UserName = "owner",
            PasswordHash = "test-hash",
            Role = UserRole.Owner
        };
        var clerk = new AppUser
        {
            DisplayName = "Clerk",
            UserName = "clerk",
            PasswordHash = "test-hash",
            Role = UserRole.BillingClerk
        };
        database.Context.AppUsers.AddRange(owner, clerk);
        database.Context.LicenceRecords.Add(new LicenceRecord
        {
            LicenceType = "20",
            LicenceNumber = "RET-001",
            ExpiresOn = today.AddDays(30)
        });
        await database.Context.SaveChangesAsync();

        var service = new BusinessModeService(new UnitOfWork(database.Context));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangeModeAsync(BusinessMode.Both, owner.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ChangeModeAsync(BusinessMode.Retail, clerk.Id));

        database.Context.LicenceRecords.Add(new LicenceRecord
        {
            LicenceType = "WHOLESALE-A",
            LicenceNumber = "WHO-001",
            ExpiresOn = today.AddDays(30)
        });
        await database.Context.SaveChangesAsync();

        await service.ChangeModeAsync(BusinessMode.Both, owner.Id);

        Assert.Equal(BusinessMode.Both, profile.BusinessMode);
        Assert.Contains(
            await database.Context.AuditLogs.ToListAsync(),
            log => log.Action == "BusinessModeChanged");
    }
}
