using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// The shop's database runs in write-ahead-log mode, so a bill saved on one counter never blocks another. New changes
// sit in pharmabill.db-wal until a checkpoint: backups must still contain them, and a restore must not let SQLite
// replay an old log onto the restored database.
[Collection(RunsAlone.Name)]
public sealed class SqliteWalTests
{
    private const string Password = "Srinagar-2026";

    [Fact]
    public async Task EveryConnection_RunsInWalMode_WithNormalSync()
    {
        await WithShopAsync(async (storage, _) =>
        {
            await using PharmaBillDbContext context = FolderBackupTests.OpenContext(storage);
            await FolderBackupTests.InitializeAsync(context, storage);
            Assert.Equal("wal", await ScalarAsync(context, "PRAGMA journal_mode;"));
            // 1 = NORMAL: fsync at checkpoints, safe against an app crash.
            Assert.Equal("1", await ScalarAsync(context, "PRAGMA synchronous;"));
        });
    }

    [Fact]
    public async Task Backup_ContainsChangesStillInTheLog_AndIsOneSelfContainedFile()
    {
        await WithShopAsync(async (storage, root) =>
        {
            using ServiceProvider services = FolderBackupTests.BuildServices(storage);
            await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(storage))
            {
                await FolderBackupTests.InitializeAsync(context, storage);
                await FolderBackupTests.SeedShopAsync(context, storage);
            }
            // Another connection stays open (as the keep-warm connection does), so nothing checkpoints on close:
            // the next change exists only in the -wal file when the backup runs.
            await using SqliteConnection holder = OpenRaw(storage);
            await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(storage))
            {
                context.Suppliers.Add(new Supplier { Name = "Only In The Log Distributors" });
                await context.SaveChangesAsync();
            }
            Assert.True(new FileInfo(storage.DatabasePath + "-wal").Length > 0, "the change was expected to still be in the log");

            BackupSettingsStore settings = services.GetRequiredService<BackupSettingsStore>();
            settings.Save(settings.Load() with { PrimaryFolder = Path.Combine(root, "backups"), Password = Password });
            string backup = await services.GetRequiredService<FolderBackupService>().BackUpNowAsync();
            await holder.CloseAsync();

            // A new PC restores it: the change from the log is there, and the restored file stands on its own.
            var newPc = new DatabaseStorageOptions(Path.Combine(root, "new-pc"));
            Directory.CreateDirectory(newPc.DataDirectory);
            using ServiceProvider newPcServices = FolderBackupTests.BuildServices(newPc);
            await using (PharmaBillDbContext fresh = FolderBackupTests.OpenContext(newPc))
            {
                await FolderBackupTests.InitializeAsync(fresh, newPc);
            }
            SqliteConnection.ClearAllPools();
            await newPcServices.GetRequiredService<FolderBackupService>().RestoreAsync(backup, Password);
            SqliteConnection.ClearAllPools();
            Assert.False(File.Exists(newPc.DatabasePath + "-wal") && new FileInfo(newPc.DatabasePath + "-wal").Length > 0, "the restored database came with a log");
            await using PharmaBillDbContext restored = FolderBackupTests.OpenContext(newPc);
            Assert.True(await restored.Suppliers.AnyAsync(s => s.Name == "Only In The Log Distributors"), "the change still in the log was not in the backup");
            Assert.Equal("ok", await ScalarAsync(restored, "PRAGMA integrity_check;"));
        });
    }

    [Fact]
    public async Task Restore_OverADatabaseWithAnOldLog_DoesNotReplayIt()
    {
        await WithShopAsync(async (storage, root) =>
        {
            using ServiceProvider services = FolderBackupTests.BuildServices(storage);
            await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(storage))
            {
                await FolderBackupTests.InitializeAsync(context, storage);
                await FolderBackupTests.SeedShopAsync(context, storage);
            }
            BackupSettingsStore settings = services.GetRequiredService<BackupSettingsStore>();
            settings.Save(settings.Load() with { PrimaryFolder = Path.Combine(root, "backups"), Password = Password });
            string backup = await services.GetRequiredService<FolderBackupService>().BackUpNowAsync();

            // After the backup, a change lands in the log, and the PC goes down before it is checkpointed: what is left
            // on disk is the database plus a -wal holding that change (simulated by keeping a copy of the log).
            string staleLog = Path.Combine(root, "stale.wal");
            await using (SqliteConnection holder = OpenRaw(storage))
            {
                await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(storage))
                {
                    context.Suppliers.Add(new Supplier { Name = "After The Backup Traders" });
                    await context.SaveChangesAsync();
                }
                File.Copy(storage.DatabasePath + "-wal", staleLog);
            }
            SqliteConnection.ClearAllPools();
            File.Copy(staleLog, storage.DatabasePath + "-wal", overwrite: true);

            await services.GetRequiredService<FolderBackupService>().RestoreAsync(backup, Password);
            SqliteConnection.ClearAllPools();

            // The restored database is exactly the backup, whatever log the old database left behind. (Backups are
            // written as one self-contained file in rollback-journal mode, which SQLite never pairs with a -wal; the
            // restore also moves the old log aside with the old database, so a rollback gets back the matching pair.)
            await using PharmaBillDbContext restored = FolderBackupTests.OpenContext(storage);
            Assert.False(await restored.Suppliers.AnyAsync(s => s.Name == "After The Backup Traders"));
            Assert.Equal("ok", await ScalarAsync(restored, "PRAGMA integrity_check;"));
            Assert.NotEmpty(await restored.Sales.ToListAsync());
        });
    }

    private static SqliteConnection OpenRaw(DatabaseStorageOptions storage)
    {
        string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(storage.KeyPath).GetOrCreateKey());
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Password = password, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static async Task<string?> ScalarAsync(PharmaBillDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync();
        }
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task WithShopAsync(Func<DatabaseStorageOptions, string, Task> body)
    {
        string root = Path.Combine(Path.GetTempPath(), $"PharmaBill.Wal.{Guid.NewGuid():N}");
        try
        {
            var storage = new DatabaseStorageOptions(Path.Combine(root, "data-root"));
            Directory.CreateDirectory(storage.DataDirectory);
            await body(storage, root);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
