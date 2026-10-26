using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class FolderBackupTests
{
    private const string Password = "shop-backup-pass-2026";

    [Fact]
    public async Task BackUpNow_Wipe_Restore_KeepsBillsStockAndScheduleRegister()
    {
        string root = Path.Combine(Path.GetTempPath(), $"PharmaBill.FolderBackup.{Guid.NewGuid():N}");
        string data = Path.Combine(root, "data-root");
        string backupFolder = Path.Combine(root, "OneDrive", "Shop backups");
        try
        {
            var storage = new DatabaseStorageOptions(data);
            Directory.CreateDirectory(storage.DataDirectory);
            using ServiceProvider services = BuildServices(storage);
            var settings = services.GetRequiredService<BackupSettingsStore>();
            var folderBackup = services.GetRequiredService<FolderBackupService>();

            // A shop with stock, three bills and a Schedule H1 sale that writes the register.
            Before before;
            await using (var context = OpenContext(storage))
            {
                await InitializeAsync(context, storage);
                before = await SeedShopAsync(context, storage);
            }

            // Without a password and a folder there is no backup nobody can restore.
            await Assert.ThrowsAsync<InvalidOperationException>(() => folderBackup.BackUpNowAsync());
            settings.Save(settings.Load() with { PrimaryFolder = backupFolder });
            await Assert.ThrowsAsync<InvalidOperationException>(() => folderBackup.BackUpNowAsync());
            settings.Save(settings.Load() with { Password = Password });

            string first = await folderBackup.BackUpNowAsync();
            string second = await folderBackup.BackUpNowAsync();
            Assert.NotEqual(first, second);
            Assert.True(File.Exists(first) && File.Exists(second), "an earlier backup was overwritten");
            Assert.StartsWith("Srinagar Medicos ", Path.GetFileName(first));
            Assert.EndsWith(".pbbak", first);
            Assert.False((await File.ReadAllBytesAsync(first)).AsSpan().StartsWith("PK"u8), "backup must not be a readable zip");
            Assert.NotNull(settings.Load().LastBackupAtUtc);

            // The PC dies: database, key, prescriptions and saved settings are all gone. Only the backup
            // folder (synced off-site) survives. A new PC starts empty in a different data folder.
            SqliteConnection.ClearAllPools();
            Directory.Delete(data, recursive: true);
            var newPc = new DatabaseStorageOptions(Path.Combine(root, "new-pc-data"));
            Directory.CreateDirectory(newPc.DataDirectory);
            using ServiceProvider newPcServices = BuildServices(newPc);
            await using (var fresh = OpenContext(newPc))
            {
                await InitializeAsync(fresh, newPc);
                Assert.Equal(0, await fresh.Sales.CountAsync());
            }
            var restore = newPcServices.GetRequiredService<FolderBackupService>();
            Assert.Null(newPcServices.GetRequiredService<BackupSettingsStore>().Load().Password);

            // A wrong or missing password is refused and changes nothing; the right one restores.
            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => restore.RestoreAsync(second, "not-the-password"));
            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() => restore.RestoreAsync(second, null));
            BackupRestoreResult result = await restore.RestoreAsync(second, Password);
            Assert.True(File.Exists(result.SafetyBackupPath));
            SqliteConnection.ClearAllPools();

            await using (var restored = OpenContext(newPc))
            {
                Assert.Equal(before.InvoiceNumbers, await restored.Sales.OrderBy(s => s.InvoiceNo).Select(s => s.InvoiceNo).ToListAsync());
                Assert.Equal(before.SalesTotal, (await restored.Sales.Select(s => s.TotalAmount).ToListAsync()).Sum());
                Assert.Equal(before.StockOnHand, await StockOnHandAsync(restored));
                ScheduleRegisterEntry entry = await restored.ScheduleRegisterEntries.SingleAsync(e => e.SaleId != null);
                Assert.Equal(before.RegisterSaleId, entry.SaleId);
                Assert.Equal(before.RegisterEntry, Describe(entry));
                Assert.Equal(before.RegisterEntryCount, await restored.ScheduleRegisterEntries.CountAsync());
                string? prescription = await restored.Sales.Where(s => s.PrescriptionFilePath != null).Select(s => s.PrescriptionFilePath).SingleAsync();
                Assert.StartsWith(newPc.RootDirectory, prescription);
                Assert.True(File.Exists(prescription), "prescription scan did not come back: " + prescription);
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
    public async Task Restore_OpensOlderUsbBackupsMadeWithTheDerivedPassword()
    {
        string root = Path.Combine(Path.GetTempPath(), $"PharmaBill.FolderBackup.{Guid.NewGuid():N}");
        try
        {
            var storage = new DatabaseStorageOptions(root);
            Directory.CreateDirectory(storage.DataDirectory);
            using ServiceProvider services = BuildServices(storage);
            await using (var context = OpenContext(storage))
            {
                await InitializeAsync(context, storage);
                context.Drugs.Add(new Drug { Name = "Made before backup passwords" });
                await context.SaveChangesAsync();
                string legacy = FolderBackupService.LegacyDerivedPasswords(storage.RootDirectory).Single();
                await new EncryptedBackupService(context, storage, services.GetRequiredService<DatabaseEncryptionKeyProvider>())
                    .CreateBackupAsync(Path.Combine(root, "old-usb.pbbak"), legacy);
                context.Drugs.Add(new Drug { Name = "Added after" });
                await context.SaveChangesAsync();
            }
            SqliteConnection.ClearAllPools();

            await services.GetRequiredService<FolderBackupService>().RestoreAsync(Path.Combine(root, "old-usb.pbbak"), typedPassword: null);
            SqliteConnection.ClearAllPools();
            await using var restored = OpenContext(storage);
            Assert.Equal(["Made before backup passwords"], await restored.Drugs.Select(d => d.Name).ToListAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("Srinagar Medicos", "Srinagar Medicos 2026-04-01 00-30-05.pbbak")]
    [InlineData("A/B: Chemists?*", "A B Chemists 2026-04-01 00-30-05.pbbak")]
    [InlineData("  ", "Pharma Remit 2026-04-01 00-30-05.pbbak")]
    [InlineData(null, "Pharma Remit 2026-04-01 00-30-05.pbbak")]
    public void BackupFileName_IsShopNameThenSortableDateTime(string? shop, string expected)
    {
        Assert.Equal(expected, FolderBackupService.BackupFileName(shop, new DateTime(2026, 4, 1, 0, 30, 5)));
    }

    [Fact]
    public void UniquePath_NeverReusesAnExistingFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"PharmaBill.FolderBackup.{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, "Shop 2026-04-01 00-30-05.pbbak"), "x");
            Assert.Equal(Path.Combine(folder, "Shop 2026-04-01 00-30-05 (2).pbbak"), FolderBackupService.UniquePath(folder, "Shop 2026-04-01 00-30-05.pbbak"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Reminder_CountsDaysSinceTheNewestFolderOrUsbBackup()
    {
        DateTime now = new(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc);
        Assert.False(BackupReminder.Evaluate(now.AddDays(-7), null, null, now).IsDue);
        BackupReminderState stale = BackupReminder.Evaluate(now.AddDays(-12), now.AddDays(-9), null, now);
        Assert.True(stale.IsDue);
        Assert.Equal(9, stale.DaysSinceLastBackup);
        Assert.Contains("9 days ago", stale.Message);
        Assert.False(BackupReminder.Evaluate(now.AddDays(-30), now.AddDays(-1), null, now).IsDue);
        Assert.True(BackupReminder.Evaluate(null, null, null, now).IsDue);
        Assert.False(BackupReminder.Evaluate(null, null, now.AddHours(3), now).IsDue);
    }

    internal sealed record Before(List<string> InvoiceNumbers, decimal SalesTotal, List<string> StockOnHand, Guid? RegisterSaleId, string RegisterEntry, int RegisterEntryCount);

    internal static string Describe(ScheduleRegisterEntry e) =>
        $"{e.RegisterType}|{e.BatchNo}|{e.Quantity:0}|{e.PatientName}|{e.PrescriberName}|{e.PrescriberRegistrationNumber}|{e.EntryAtUtc:yyyy-MM-dd HH:mm:ss.fffffff}";

    internal static async Task<Before> SeedShopAsync(PharmaBillDbContext context, DatabaseStorageOptions storage)
    {
        context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Srinagar Medicos", BusinessMode = BusinessMode.Retail, InvoicePrefix = "SM", State = "Jammu and Kashmir" });
        await context.SaveChangesAsync();
        var uow = new UnitOfWork(context);
        var entitlements = new Allowed();
        var branches = new BranchService(uow, new BranchSettingsStore(storage), storage);
        var purchases = new PurchaseService(uow, entitlements, new StorageLocationService(context), branches, new NullLicenseRuntimeGuard());
        var addStock = new AddStockService(uow, purchases, entitlements, new NullLicenseRuntimeGuard());
        Guid owner = Guid.NewGuid();
        Supplier supplier = await addStock.AddSupplierAsync("Valley Distributors", owner, UserRole.Owner);
        DateOnly today = BusinessClock.Today;
        AddStockResult paracetamol = await addStock.AddStockAsync(new AddStockInput(null, null, "Paracetamol 650", "Paracetamol", "Acme", "G", "PCM-01",
            today.AddYears(2), 30m, 18m, 100m, 0m, 10m, 5m, supplier.Id, "VD-1001", today, "A1", HsnCode: "30049099"), owner, UserRole.Owner);
        AddStockResult alprazolam = await addStock.AddStockAsync(new AddStockInput(null, null, "Alprazolam 0.5", "Alprazolam", "Acme", "H1", "ALP-07",
            today.AddYears(2), 40m, 25m, 20m, 0m, 10m, 12m, supplier.Id, "VD-1002", today, "C3", HsnCode: "30049099"), owner, UserRole.Owner);

        var billing = new RetailBillingService(uow, new NumberSeriesService(uow), entitlements, new CatalogSearchService(context, branches),
            new StorageLocationService(context), branches, new NullLicenseRuntimeGuard(), Path.Combine(storage.RootDirectory, "prescriptions"));
        foreach (decimal quantity in new[] { 2m, 5m, 1m })
        {
            await billing.SaveSaleAsync(new SaveRetailSaleInput("Walk-in", "9876543210", null, null, null, null,
                [new RetailSaleLineInput(paracetamol.DrugId, paracetamol.BatchId, quantity, 0m, 30m)], [new RetailPaymentInput("Cash", quantity * 30m)], false),
                owner, UserRole.Owner);
        }
        string scan = Path.Combine(Path.GetTempPath(), $"rx-{Guid.NewGuid():N}.jpg");
        await File.WriteAllBytesAsync(scan, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4]);
        try
        {
            await billing.SaveSaleAsync(new SaveRetailSaleInput("Rahul Kumar", "9876500011", "4 Lal Chowk, Srinagar", "Dr A. Shah", "JKMC-4411", scan,
                [new RetailSaleLineInput(alprazolam.DrugId, alprazolam.BatchId, 10m, 0m, 40m)], [new RetailPaymentInput("Cash", 400m)], false),
                owner, UserRole.Owner);
        }
        finally
        {
            File.Delete(scan);
        }

        ScheduleRegisterEntry entry = await context.ScheduleRegisterEntries.SingleAsync(e => e.SaleId != null);
        Assert.Equal("Dr A. Shah", entry.PrescriberName);
        List<string> stock = await StockOnHandAsync(context);
        Assert.Equal(["ALP-07=10", "PCM-01=92"], stock);
        return new Before(
            await context.Sales.OrderBy(s => s.InvoiceNo).Select(s => s.InvoiceNo).ToListAsync(),
            (await context.Sales.Select(s => s.TotalAmount).ToListAsync()).Sum(),
            stock,
            entry.SaleId,
            Describe(entry),
            await context.ScheduleRegisterEntries.CountAsync());
    }

    internal static async Task<List<string>> StockOnHandAsync(PharmaBillDbContext context)
    {
        var rows = await context.StockMovements.Join(context.Batches, m => m.BatchId, b => b.Id, (m, b) => new { b.BatchNo, m.QuantityChange }).ToListAsync();
        return rows.GroupBy(r => r.BatchNo).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}={g.Sum(r => r.QuantityChange):0}").ToList();
    }

    internal static ServiceProvider BuildServices(DatabaseStorageOptions storage)
    {
        var services = new ServiceCollection();
        services.AddSingleton(storage);
        services.AddSingleton(new DatabaseEncryptionKeyProvider(storage.KeyPath));
        services.AddScoped(_ => OpenContext(storage));
        services.AddScoped<EncryptedBackupService>();
        services.AddSingleton<BackupSettingsStore>();
        services.AddSingleton<FolderBackupService>();
        return services.BuildServiceProvider();
    }

    // Opens whatever key is on disk now, as the app does at startup - after a restore that is the backup's key.
    internal static PharmaBillDbContext OpenContext(DatabaseStorageOptions storage)
    {
        SQLitePCL.Batteries_V2.Init();
        Directory.CreateDirectory(Path.GetDirectoryName(storage.DatabasePath)!);
        string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(storage.KeyPath).GetOrCreateKey());
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Password = password, Pooling = false }.ToString())
            .AddInterceptors(new SaveChangesAuditInterceptor(deviceId), SqliteWalInterceptor.Instance)
            .Options;
        return new PharmaBillDbContext(options, deviceId);
    }

    internal static Task InitializeAsync(PharmaBillDbContext context, DatabaseStorageOptions storage)
    {
        var branches = new BranchService(new UnitOfWork(context), new BranchSettingsStore(storage), storage);
        return new DbInitializer(context, new StorageLocationService(context), branches, storage).InitializeAsync();
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
