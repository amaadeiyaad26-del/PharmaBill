using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Sync.Terminals;
using Xunit;

namespace PharmaBill.Tests;

// The one gate on a database file: ordinary work shares it, replacing the file (restore, a fresh store) holds it alone.
// A database is never created by opening a missing file. Runs alone: it reads connection pools and holds the gate.
[Collection(RunsAlone.Name)]
public sealed class DatabaseMaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.Maintenance.{Guid.NewGuid():N}");

    public void Dispose()
    {
        DatabaseMaintenance.DrainTimeout = TimeSpan.FromSeconds(30);
        if (Directory.Exists(_root))
        {
            foreach (string db in Directory.GetFiles(_root, "*.db", SearchOption.AllDirectories))
            {
                DatabaseMaintenance.ClearPools(db);
            }
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<DatabaseStorageOptions> ShopAsync(string name = "shop")
    {
        var storage = new DatabaseStorageOptions(Path.Combine(_root, name));
        Directory.CreateDirectory(storage.DataDirectory);
        await using PharmaBillDbContext context = FolderBackupTests.OpenContext(storage);
        await FolderBackupTests.InitializeAsync(context, storage);
        return storage;
    }

    [Fact]
    public async Task Replacing_WaitsForWorkInFlight_AndRefusesNewWork_WithTheReason()
    {
        DatabaseStorageOptions storage = await ShopAsync();
        await using PharmaBillDbContext busy = FolderBackupTests.OpenContext(storage);
        await busy.Database.OpenConnectionAsync();
        Assert.Equal(1, DatabaseMaintenance.ActiveCount(storage.DatabasePath));

        bool bodyRan = false;
        var started = new TaskCompletionSource();
        Task restore = DatabaseMaintenance.RunExclusiveAsync(storage.DatabasePath, "a backup is being restored", () =>
        {
            bodyRan = true;
            return Task.CompletedTask;
        });
        while (!DatabaseMaintenance.IsHeld(storage.DatabasePath))
        {
            await Task.Delay(10);
        }

        // New work is refused while the file is held, and says why.
        await using (PharmaBillDbContext late = FolderBackupTests.OpenContext(storage))
        {
            var refused = await Assert.ThrowsAsync<DatabaseUnavailableException>(() => late.Sales.CountAsync());
            Assert.Contains("a backup is being restored", refused.Message);
        }
        // The work in flight is waited for, not cut off.
        await Task.Delay(200);
        Assert.False(bodyRan);
        Assert.Equal(0, await busy.Sales.CountAsync());

        await busy.Database.CloseConnectionAsync();
        await restore;
        Assert.True(bodyRan);
        Assert.False(DatabaseMaintenance.IsHeld(storage.DatabasePath));
        await using PharmaBillDbContext after = FolderBackupTests.OpenContext(storage);
        Assert.Equal(0, await after.Sales.CountAsync());
    }

    [Fact]
    public async Task Replacing_GivesUp_WhenWorkDoesNotFinish_AndChangesNothing()
    {
        DatabaseStorageOptions storage = await ShopAsync();
        DatabaseMaintenance.DrainTimeout = TimeSpan.FromMilliseconds(300);
        await using PharmaBillDbContext busy = FolderBackupTests.OpenContext(storage);
        await busy.Database.OpenConnectionAsync();

        bool bodyRan = false;
        var timedOut = await Assert.ThrowsAsync<DatabaseUnavailableException>(() =>
            DatabaseMaintenance.RunExclusiveAsync(storage.DatabasePath, "a backup is being restored", () =>
            {
                bodyRan = true;
                return Task.CompletedTask;
            }));
        Assert.Contains("1 database operation is still running", timedOut.Message);
        Assert.Contains("Nothing was changed", timedOut.Message);
        Assert.False(bodyRan);
        Assert.False(DatabaseMaintenance.IsHeld(storage.DatabasePath));
        Assert.Equal(0, await busy.Sales.CountAsync());
    }

    [Fact]
    public async Task ClearingPools_TouchesOnlyThatDatabase()
    {
        DatabaseStorageOptions first = await ShopAsync("first");
        DatabaseStorageOptions second = await ShopAsync("second");
        string pooledFirst = Pooled(first), pooledSecond = Pooled(second);
        object firstPool = Pool(pooledFirst), secondPool = Pool(pooledSecond);

        DatabaseMaintenance.ClearPools(first.DatabasePath);

        Assert.True(IsCleared(firstPool), "the database being replaced kept its pool");
        Assert.False(IsCleared(secondPool), "another database's pool was cleared");
    }

    [Fact]
    public async Task OpeningAMissingFile_Fails_AndCreatesNothing()
    {
        var storage = new DatabaseStorageOptions(Path.Combine(_root, "none"));
        Directory.CreateDirectory(storage.DataDirectory);
        await using PharmaBillDbContext context = FolderBackupTests.OpenContext(storage);

        var refused = await Assert.ThrowsAsync<DatabaseUnavailableException>(() => context.Sales.CountAsync());
        Assert.Contains("missing", refused.Message);
        Assert.Contains("Nothing was created", refused.Message);
        Assert.False(File.Exists(storage.DatabasePath));
    }

    [Fact]
    public async Task Startup_CreatesTheFirstDatabase_ThenRefusesToReplaceAMissingOne()
    {
        DatabaseStorageOptions storage = await ShopAsync();
        Assert.True(File.Exists(storage.DatabasePath));
        Assert.True(DatabaseCreation.WasCreatedBefore(storage.DatabasePath));

        // The file goes missing (moved, drive gone). A fresh process must stop, not open an empty shop.
        var gone = new DatabaseStorageOptions(Path.Combine(_root, "gone"));
        Directory.CreateDirectory(gone.DataDirectory);
        File.WriteAllText(gone.DatabasePath + ".created", "existed");
        await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(gone))
        {
            var missing = await Assert.ThrowsAsync<DatabaseMissingException>(() => FolderBackupTests.InitializeAsync(context, gone));
            Assert.Equal(Path.GetFullPath(gone.DatabasePath), missing.DatabasePath);
        }
        Assert.False(File.Exists(gone.DatabasePath));

        // No packaged seed is copied in its place either.
        AppDataPaths.EnsureSeedDatabaseIfMissing(gone.DatabasePath);
        Assert.False(File.Exists(gone.DatabasePath));

        // The owner chose to start again: a new, empty database is created deliberately.
        DatabaseCreation.Forget(gone.DatabasePath);
        await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(gone))
        {
            await FolderBackupTests.InitializeAsync(context, gone);
            Assert.Equal(0, await context.Sales.CountAsync());
        }
        Assert.True(DatabaseCreation.WasCreatedBefore(gone.DatabasePath));
    }

    [Fact]
    public async Task Startup_ReplacesAnEmptyFile_ItHasMovedAside()
    {
        // A 0-byte file holds no records: it is moved aside and a fresh database takes its place, as before.
        var storage = new DatabaseStorageOptions(Path.Combine(_root, "empty"));
        Directory.CreateDirectory(storage.DataDirectory);
        File.WriteAllText(storage.DatabasePath + ".created", "existed");
        File.WriteAllBytes(storage.DatabasePath, []);
        await using PharmaBillDbContext context = FolderBackupTests.OpenContext(storage);
        await FolderBackupTests.InitializeAsync(context, storage);
        Assert.Equal(0, await context.Sales.CountAsync());
        Assert.NotEmpty(Directory.GetFiles(storage.DataDirectory, "pharmabill.db.empty*"));
    }

    [Fact]
    public void CountersWarning_NamesEachCounter_AndCountsWhoIsSignedIn()
    {
        string directory = Path.Combine(_root, "terminals");
        Directory.CreateDirectory(directory);
        var devices = new TerminalDeviceStore(directory);
        var sessions = new TerminalSessions(directory);
        var asked = new Recorded(answer: false);
        var counters = new CountersInUse(sessions, devices, asked);

        // Nobody signed in: no question at all.
        Assert.Null(counters.Warning("Restoring a backup", "Bills will be replaced."));
        Assert.True(counters.ConfirmProceed("Restoring a backup", "Bills will be replaced."));
        Assert.Empty(asked.Messages);

        TerminalDevice front = devices.Register("Front counter").Device;
        TerminalDevice back = devices.Register("Back counter").Device;
        sessions.Start(front.Id, User("asha", "Asha"));
        sessions.Start(front.Id, User("ravi", "Ravi"));
        sessions.Start(back.Id, User("meena", "Meena"));

        string warning = counters.Warning("Restoring a backup", "Bills will be replaced.")!;
        Assert.Contains("2 billing counters are connected to this server, with 3 people signed in", warning);
        Assert.Contains("Front counter: Asha, Ravi", warning);
        Assert.Contains("Back counter: Meena", warning);
        Assert.Contains("Restoring a backup now stops their billing", warning);

        // The owner says no: nothing goes ahead.
        Assert.False(counters.ConfirmProceed("Restoring a backup", "Bills will be replaced."));
        Assert.Single(asked.Messages);
    }

    [Fact]
    public void Restore_IsNotStarted_WhenTheOwnerDeclinesBecauseCountersAreBilling()
    {
        string directory = Path.Combine(_root, "terminals");
        Directory.CreateDirectory(directory);
        var devices = new TerminalDeviceStore(directory);
        var sessions = new TerminalSessions(directory);
        sessions.Start(devices.Register("Front counter").Device.Id, User("asha", "Asha"));
        var asked = new Recorded(answer: false);

        Ui.Run(async () =>
        {
            var storage = new DatabaseStorageOptions(Path.Combine(_root, "page"));
            Directory.CreateDirectory(storage.DataDirectory);
            var backup = Path.Combine(_root, "shop.pbbak");
            File.WriteAllText(backup, "not opened");
            using Microsoft.Extensions.DependencyInjection.ServiceProvider services = FolderBackupTests.BuildServices(storage);
            T Get<T>() where T : notnull => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<T>(services);
            var page = new PharmaBill.App.ViewModels.BackupPageViewModel(Get<PharmaBill.Data.Services.BackupSettingsStore>(), Get<PharmaBill.Data.Services.FolderBackupService>(),
                new PharmaBill.Data.Services.UsbBackupService(Get<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), new PharmaBill.Data.Services.UsbBackupSettingsStore(storage), Get<PharmaBill.Data.Services.BackupSettingsStore>(), storage),
                new PharmaBill.Data.Services.UsbBackupSettingsStore(storage), new BackupPageEndToEndTests.FakePicker { BackupFile = backup }, asked, new CurrentSession(),
                new CountersInUse(sessions, devices, asked));
            page.RestorePassword = "eleven-chars";
            await page.RestoreBackupCommand.ExecuteAsync(null);

            Assert.Contains("Front counter: Asha", Assert.Single(asked.Messages));
            Assert.Equal(string.Empty, page.ErrorMessage);
            Assert.DoesNotContain("Restored", page.StatusMessage);
        });
    }

    private static AppUser User(string userName, string displayName) =>
        new() { Id = Guid.NewGuid(), UserName = userName, DisplayName = displayName, Role = UserRole.BillingClerk };

    private sealed class Recorded(bool answer) : IConfirmationService
    {
        public List<string> Messages { get; } = [];
        public bool Confirm(string message, string title)
        {
            Messages.Add(message);
            return answer;
        }
        public bool? Choose(string message, string title, string acceptLabel, string alternateLabel) => answer;
        public void Notify(string title, string message) { }
        public void NotifyError(string title, string message) { }
    }

    // A pooled connection string for this database, with one idle connection left in its pool.
    private static string Pooled(DatabaseStorageOptions storage)
    {
        string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(storage.KeyPath).GetOrCreateKey());
        string connectionString = new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Password = password, Mode = SqliteOpenMode.ReadWrite }.ToString();
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        SqliteWal.Apply(connection);
        return connectionString;
    }

    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private static object Pool(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        object inner = Field(connection, "SqliteConnectionInternal");
        return Field(inner, "SqliteConnectionPool");
    }

    private static object Field(object target, string typeName) =>
        target.GetType().GetFields(Private).Select(field => field.GetValue(target)).FirstOrDefault(value => value?.GetType().Name == typeName)
            ?? throw new InvalidOperationException($"{target.GetType().Name} no longer holds a {typeName}.");

    private static bool IsCleared(object pool) =>
        pool.GetType().GetField("_state", Private)?.GetValue(pool)?.ToString() == "Disabled";
}
