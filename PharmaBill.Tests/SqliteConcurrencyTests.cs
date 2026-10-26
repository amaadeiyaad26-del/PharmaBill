using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using Xunit;

namespace PharmaBill.Tests;

// Several counters saving at once, each request on a fresh connection to the encrypted database (as a server PC
// serving billing counters does). In rollback-journal mode a connection opened while another commits fails at once
// with "database is locked"; in WAL mode opening never waits for a writer, and writers queue for the busy timeout.
[Collection(RunsAlone.Name)]
public sealed class SqliteConcurrencyTests
{
    // The app's connection string: pooled (the encrypted key is derived once per pooled connection), as the server PC runs.
    private static PharmaBillDbContext OpenPooled(DatabaseStorageOptions storage)
    {
        string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(storage.KeyPath).GetOrCreateKey());
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        return new PharmaBillDbContext(new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Password = password }.ToString())
            .AddInterceptors(new SaveChangesAuditInterceptor(deviceId), SqliteWalInterceptor.Instance).Options, deviceId);
    }

    // The window that failed before: one counter's save is committing (it holds the database's write lock) while
    // another counter opens a connection. Opening an encrypted database checks the key with a read; in rollback-journal
    // mode that read must wait for the commit and failed with "database is locked". In WAL mode it does not wait.
    [Fact]
    public async Task OpeningAndReading_WhileAnotherCounterCommits_DoesNotWait()
    {
        string root = Path.Combine(Path.GetTempPath(), $"PharmaBill.Concurrency.{Guid.NewGuid():N}");
        try
        {
            var storage = new DatabaseStorageOptions(root);
            Directory.CreateDirectory(storage.DataDirectory);
            await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(storage))
            {
                await FolderBackupTests.InitializeAsync(context, storage);
            }
            string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(storage.KeyPath).GetOrCreateKey());

            // The other counter, mid-commit: an exclusive write lock, held.
            await using var committing = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Password = password, Pooling = false }.ToString());
            await committing.OpenAsync();
            await SqliteWal.ApplyAsync(committing);
            await using (SqliteCommand begin = committing.CreateCommand())
            {
                begin.CommandText = "BEGIN EXCLUSIVE;";
                await begin.ExecuteNonQueryAsync();
            }

            // This counter opens a fresh connection and reads, without waiting more than a moment.
            var reading = new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Password = password, Pooling = false, DefaultTimeout = 2 }.ToString();
            var deviceId = new DatabaseDeviceId(Guid.NewGuid());
            await using var counter = new PharmaBillDbContext(new DbContextOptionsBuilder<PharmaBillDbContext>()
                .UseSqlite(reading).AddInterceptors(new SaveChangesAuditInterceptor(deviceId), SqliteWalInterceptor.Instance).Options, deviceId);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int suppliers = await counter.Suppliers.CountAsync();
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1.5), $"the read waited {watch.Elapsed.TotalSeconds:0.0} s for the other counter's commit");
            Assert.True(suppliers >= 0);
            await using (SqliteCommand rollback = committing.CreateCommand())
            {
                rollback.CommandText = "ROLLBACK;";
                await rollback.ExecuteNonQueryAsync();
            }
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

    [Fact]
    public async Task ManyCountersSavingAtOnce_NeverHitDatabaseLocked()
    {
        string root = Path.Combine(Path.GetTempPath(), $"PharmaBill.Concurrency.{Guid.NewGuid():N}");
        try
        {
            var storage = new DatabaseStorageOptions(root);
            Directory.CreateDirectory(storage.DataDirectory);
            await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(storage))
            {
                await FolderBackupTests.InitializeAsync(context, storage);
            }

            const int counters = 6;
            const int billsEach = 15;
            var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
            await Task.WhenAll(Enumerable.Range(0, counters).Select(counter => Task.Run(async () =>
            {
                for (int bill = 0; bill < billsEach; bill++)
                {
                    try
                    {
                        // A fresh connection per request, opened while the other counters are writing.
                        await using PharmaBillDbContext context = OpenPooled(storage);
                        await using var transaction = await context.Database.BeginTransactionAsync();
                        context.Suppliers.Add(new Supplier { Name = $"Counter {counter} bill {bill}" });
                        await context.SaveChangesAsync();
                        await transaction.CommitAsync();
                    }
                    catch (Exception ex)
                    {
                        failures.Add(ex.GetBaseException().Message);
                    }
                }
            })));

            Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Distinct().Take(5)));
            await using PharmaBillDbContext check = FolderBackupTests.OpenContext(storage);
            Assert.Equal(counters * billsEach, await check.Suppliers.CountAsync(s => s.Name.StartsWith("Counter ")));
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
