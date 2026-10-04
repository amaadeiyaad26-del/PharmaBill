using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Sync;
using Xunit;

namespace PharmaBill.Tests;

public sealed class SyncFolderExchangeTests
{
    [Fact]
    public async Task FolderSelectionAndAutoSyncSettingsPersistUnderDpapi()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PharmaBill.FolderSync.{Guid.NewGuid():N}");
        var storage = new DatabaseStorageOptions(root);
        try
        {
            var store = new SyncFolderSettingsStore(storage);
            var settings = SyncFolderSettings.Default with
            {
                FolderPath = Path.Combine(root, "chosen-cloud-folder"),
                AutoSyncEnabled = true,
                Status = "Idle",
                LastSyncAtUtc = DateTime.UtcNow,
                LastOutgoingPackages = 2,
                LastIncomingPackages = 1,
                LastAppliedChanges = 14
            };
            await store.SaveAsync(settings);

            Assert.Equal(settings, await new SyncFolderSettingsStore(storage).LoadAsync());
            Assert.Equal(
                "PharmaBill_Sync",
                Path.GetFileName(SyncFolderSettingsStore.FindDefaultFolder()));
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    [Fact]
    public async Task PollingDetectsPackageOnlyAfterStableAndArchivesItAfterApplying()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PharmaBill.FolderSync.{Guid.NewGuid():N}");
        await using var peers = await SyncPeers.CreateAsync(root);
        try
        {
            peers.Android.Context.Drugs.Add(new Drug { Name = "Folder pickup medicine" });
            await peers.Android.Context.SaveChangesAsync();
            var incoming = Path.Combine(root, "from_android", "android-package.pbsync");
            await peers.AndroidPackageService.ExportAsync(peers.WindowsId, incoming);

            Assert.Equal(0, await peers.WindowsExchange.ProcessIncomingPackagesOnceAsync(root));
            Assert.Equal(1, await peers.WindowsExchange.ProcessIncomingPackagesOnceAsync(root));

            Assert.Equal("Folder pickup medicine", (await peers.Windows.Context.Drugs.SingleAsync()).Name);
            Assert.False(File.Exists(incoming));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "archive"), "*.pbsync"));
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    [Fact]
    public async Task PollingSkipsLockedFilesAndQuarantinesStablePartialPackages()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PharmaBill.FolderSync.{Guid.NewGuid():N}");
        await using var peers = await SyncPeers.CreateAsync(root);
        try
        {
            var incomingDirectory = Path.Combine(root, "from_android");
            Directory.CreateDirectory(incomingDirectory);
            var lockedPath = Path.Combine(incomingDirectory, "locked.pbsync");
            await File.WriteAllBytesAsync(lockedPath, [1, 2, 3, 4]);
            using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Equal(0, await peers.WindowsExchange.ProcessIncomingPackagesOnceAsync(root));
                Assert.Equal(0, await peers.WindowsExchange.ProcessIncomingPackagesOnceAsync(root));
                Assert.True(File.Exists(lockedPath));
                Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "errors"), "*.pbsync"));
            }

            await Assert.ThrowsAsync<InvalidDataException>(
                () => peers.WindowsExchange.ProcessIncomingPackagesOnceAsync(root));
            Assert.False(File.Exists(lockedPath));
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "errors"), "*.pbsync"));
            Assert.False(await peers.Windows.Context.Drugs.AnyAsync());
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    [Fact]
    public async Task FolderSyncNowPublishesEncryptedWindowsChangesAndPicksUpAndroidChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PharmaBill.FolderSync.{Guid.NewGuid():N}");
        await using var peers = await SyncPeers.CreateAsync(root);
        try
        {
            peers.Windows.Context.Drugs.Add(new Drug { Name = "Windows medicine" });
            await peers.Windows.Context.SaveChangesAsync();
            var folderSettings = new SyncFolderSettingsStore(peers.WindowsStorage);
            var exchange = new SyncFolderExchangeService(
                peers.WindowsProvider.GetRequiredService<IServiceScopeFactory>(),
                folderSettings,
                peers.WindowsProvider.GetRequiredService<ILogger<SyncFolderExchangeService>>());
            await exchange.ConfigureAsync(root, autoSyncEnabled: true);
            await exchange.SyncNowAsync();

            var windowsPackage = Assert.Single(
                Directory.EnumerateFiles(Path.Combine(root, "from_windows"), "*.pbsync"));
            Assert.StartsWith("PBENC1", System.Text.Encoding.ASCII.GetString(
                await File.ReadAllBytesAsync(windowsPackage), 0, 6));
            Assert.All(
                await peers.Windows.Context.ChangeLogs.ToListAsync(),
                change => Assert.Equal(SyncState.Synced, change.SyncState));
            await peers.AndroidPackageService.ImportAsync(windowsPackage);
            Assert.Contains("Windows medicine", await peers.Android.Context.Drugs.Select(item => item.Name).ToListAsync());

            peers.Android.Context.Drugs.Add(new Drug { Name = "Android medicine" });
            await peers.Android.Context.SaveChangesAsync();
            var androidPackagePath = Path.Combine(root, "from_android", "android-return.pbsync");
            await peers.AndroidPackageService.ExportAsync(peers.WindowsId, androidPackagePath);
            Assert.Equal(0, await exchange.ProcessIncomingPackagesOnceAsync(root));
            Assert.Equal(1, await exchange.ProcessIncomingPackagesOnceAsync(root));

            Assert.Contains("Android medicine", await peers.Windows.Context.Drugs.Select(item => item.Name).ToListAsync());
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "archive"), "*.pbsync"));
            Assert.Equal("Idle", (await folderSettings.LoadAsync()).Status);
            await exchange.SyncNowAsync();
            Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "from_windows"), "*.pbsync"));
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class SyncPeers : IAsyncDisposable
    {
        private SyncPeers(
            string root,
            DatabaseTestContext windows,
            DatabaseTestContext android,
            DatabaseStorageOptions windowsStorage,
            ServiceProvider windowsProvider,
            ServiceProvider androidProvider,
            SyncFolderExchangeService windowsExchange)
        {
            Root = root;
            Windows = windows;
            Android = android;
            WindowsStorage = windowsStorage;
            WindowsProvider = windowsProvider;
            AndroidProvider = androidProvider;
            WindowsExchange = windowsExchange;
        }

        public string Root { get; }
        public DatabaseTestContext Windows { get; }
        public DatabaseTestContext Android { get; }
        public DatabaseStorageOptions WindowsStorage { get; }
        public ServiceProvider WindowsProvider { get; }
        public ServiceProvider AndroidProvider { get; }
        public SyncFolderExchangeService WindowsExchange { get; }
        public Guid WindowsId => Windows.Context.DeviceId;
        public Guid AndroidId => Android.Context.DeviceId;
        public SyncPackageService AndroidPackageService =>
            AndroidProvider.GetRequiredService<SyncPackageService>();

        public static async Task<SyncPeers> CreateAsync(string root)
        {
            Directory.CreateDirectory(root);
            var windows = await DatabaseTestContext.CreateAsync();
            var android = await DatabaseTestContext.CreateAsync();
            var windowsStorage = new DatabaseStorageOptions(Path.Combine(root, "windows-data"));
            var androidStorage = new DatabaseStorageOptions(Path.Combine(root, "android-data"));
            var windowsProvider = CreateProvider(windows, windowsStorage);
            var androidProvider = CreateProvider(android, androidStorage);
            var windowsDevices = windowsProvider.GetRequiredService<SyncDeviceService>();
            var androidDevices = androidProvider.GetRequiredService<SyncDeviceService>();
            await windowsDevices.InitializeLocalDeviceAsync("Windows");
            await androidDevices.InitializeLocalDeviceAsync("Android");
            var pairingCode = windowsDevices.CreatePairingCode().Code;
            await windowsDevices.PairDeviceAsync(android.Context.DeviceId, "Android", "Android", pairingCode);
            await androidDevices.PairDeviceAsync(windows.Context.DeviceId, "Windows", "Windows", pairingCode);

            var settingsStore = new SyncFolderSettingsStore(windowsStorage);
            var exchangeService = new SyncFolderExchangeService(
                windowsProvider.GetRequiredService<IServiceScopeFactory>(),
                settingsStore,
                windowsProvider.GetRequiredService<ILogger<SyncFolderExchangeService>>());
            return new SyncPeers(root, windows, android, windowsStorage, windowsProvider, androidProvider, exchangeService);
        }

        public async ValueTask DisposeAsync()
        {
            await WindowsProvider.DisposeAsync();
            await AndroidProvider.DisposeAsync();
            await Windows.DisposeAsync();
            await Android.DisposeAsync();
        }

        private static ServiceProvider CreateProvider(
            DatabaseTestContext database,
            DatabaseStorageOptions storage)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(database.Context);
            services.AddSingleton(new DatabaseDeviceId(database.Context.DeviceId));
            services.AddSingleton(storage);
            services.AddSingleton<ProtectedPeerKeyStore>();
            services.AddSingleton<FileTransferQueue>();
            services.AddSingleton<SyncPayloadSerializer>();
            services.AddSingleton<PharmaBill.Core.Sync.HybridLogicalClockState>();
            services.AddSingleton<HybridLogicalClock>();
            services.AddScoped<SyncDeviceService>();
            services.AddScoped<ChangeApplier>();
            services.AddScoped<SyncPackageService>();
            return services.BuildServiceProvider();
        }
    }
}
