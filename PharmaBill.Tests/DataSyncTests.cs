using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Sync;
using PharmaBill.Core.Sync;
using PharmaBill.Sync.Terminals;
using Xunit;

namespace PharmaBill.Tests;

// Backup & Sync: local backup and PC migration, and device and cloud sync, as two tabs of one screen, built from the
// real Backup and Sync pages (shown inside Settings > Data & Maintenance; the routing is in MergedScreenRoutesTests).
public sealed class DataSyncTests
{
    [Fact]
    public void Tabs_ShowTheRealBackupAndSyncPages_AndKeepTheirState()
    {
        WithShop(async shop =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            DataSyncPageViewModel page = shop.Page();
            Window host = await shop.ShowAsync(page);
            await page.LoadAsync();
            await Ui.SettleAsync();

            // One title, the two tabs under it, and the Backup page's own sections.
            List<string> texts = Texts(host);
            Assert.Single(texts, text => text == "Backup & Sync");
            Assert.DoesNotContain("Backup", texts);
            Assert.True(Equals(true, Ui.Button(host, "💾 Local Backup & PC Migration").Tag));
            Assert.Contains("4. Restore", texts);
            Ui.Button(host, "Back up now");
            Ui.Button(host, "Back up to USB");
            Ui.Button(host, "Restore from a backup file…");
            await Ui.PressAsync(Ui.Button(host, "Choose folder"));
            Assert.Equal(shop.Picker.Folder, page.Backup.BackupFolder);

            // Device & Cloud Sync: the Sync Station, as a section heading, with this PC registered as a sync device.
            await Ui.PressAsync(Ui.Button(host, "🔄 Device & Cloud Sync"));
            Assert.True(page.IsSyncTab);
            TextBlock heading = Descendants(host).OfType<TextBlock>().Single(text => text.IsVisible && text.Text == "Sync Station");
            Assert.Equal(16, heading.FontSize);
            Assert.Contains("This device ID:", Texts(host));
            Assert.True(string.IsNullOrEmpty(page.Sync.ErrorMessage), page.Sync.ErrorMessage);

            // Back to Backup: the folder chosen before is still there.
            await Ui.PressAsync(Ui.Button(host, "💾 Local Backup & PC Migration"));
            Assert.Equal(shop.Picker.Folder, page.Backup.BackupFolder);
            Assert.Equal(shop.Picker.Folder, Ui.Named<TextBox>(host, "Backup folder").Text);
            bindings.AssertNone();
            host.Close();
        });
    }

    // Settings > Data & Maintenance: Backup & Sync, the medicine data import and diagnostics, once sidebar items.
    [Fact]
    public void DataAndMaintenance_ShowsBackupImportAndDiagnostics_AndOpeningTheImportStartsNothing()
    {
        WithShop(async shop =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            var maintenance = new DataMaintenanceViewModel(shop.Page(), shop.Scopes);
            var view = new PharmaBill.App.Views.DataMaintenanceView { DataContext = maintenance };
            var mainWindow = new MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1300, Height = 1000, Resources = mainWindow.Resources };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ScrollViewer { Content = view };
            await maintenance.LoadAsync();
            await Ui.SettleAsync();

            // Data & Backup: the Backup page, under Backup & Sync's own tabs (its big title hidden).
            Assert.True(Equals(true, Ui.Button(host, "💾 Data & Backup").Tag));
            Ui.Button(host, "💾 Local Backup & PC Migration");
            Ui.Button(host, "Back up now");
            Assert.DoesNotContain("Backup & Sync", Texts(host));

            // Data Import: the import panel, with nothing running until Import is pressed.
            await Ui.PressAsync(Ui.Button(host, "📥 Data Import"));
            Assert.NotNull(maintenance.CatalogImport);
            Assert.False(maintenance.CatalogImport!.IsImporting);
            Ui.Button(host, "Import / resume");
            Assert.False(Ui.Button(host, "Cancel import").IsEnabled);
            Assert.Equal(0, await shop.CountCatalogAsync());

            // System Diagnostics: the database row counts.
            await Ui.PressAsync(Ui.Button(host, "🔧 System Diagnostics"));
            Assert.NotNull(maintenance.Diagnostics);
            await Ui.UntilAsync(() => maintenance.Diagnostics!.TableCounts.Count > 0, "the row counts");
            Assert.True(string.IsNullOrEmpty(maintenance.Diagnostics!.ErrorMessage), maintenance.Diagnostics.ErrorMessage);
            Ui.Button(host, "Refresh");
            bindings.AssertNone();
            host.Close();
        });
    }

    private static void WithShop(Func<Shop, Task> body)
    {
        string root = Path.Combine(Path.GetTempPath(), $"PharmaBill.DataSync.{Guid.NewGuid():N}");
        try
        {
            Ui.Run(async () =>
            {
                await using var shop = await Shop.OpenAsync(root);
                await body(shop);
            });
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static List<string> Texts(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text).ToList();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }

    // The real Backup and Sync pages over a database in a temporary folder. The app's own registration methods are not
    // used: they point at the real data folder. Nothing here starts a server.
    private sealed class Shop : IAsyncDisposable
    {
        private ServiceProvider _services = null!;

        public BackupPageEndToEndTests.FakePicker Picker { get; } = new();

        public static async Task<Shop> OpenAsync(string root)
        {
            var storage = new DatabaseStorageOptions(Path.Combine(root, "data"));
            Directory.CreateDirectory(storage.DataDirectory);
            await using (PharmaBillDbContext context = FolderBackupTests.OpenContext(storage))
            {
                await FolderBackupTests.InitializeAsync(context, storage);
            }
            var shop = new Shop();
            shop.Picker.Folder = Path.Combine(root, "Shop backups");
            string terminals = Path.Combine(root, "terminals");
            var session = new CurrentSession();
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton(storage)
                .AddSingleton(new DatabaseEncryptionKeyProvider(storage.KeyPath))
                .AddSingleton(new DatabaseDeviceId(Guid.NewGuid()))
                .AddScoped(_ => FolderBackupTests.OpenContext(storage))
                .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
                .AddScoped<EncryptedBackupService>()
                .AddSingleton<BackupSettingsStore>()
                .AddSingleton<FolderBackupService>()
                .AddSingleton(new UsbBackupSettingsStore(storage))
                .AddSingleton<UsbBackupService>()
                .AddSingleton(session)
                .AddSingleton<IConfirmationService, BackupPageEndToEndTests.AlwaysYes>()
                .AddSingleton<IFilePickerService>(shop.Picker)
                // As AddPharmaBillSync registers them, without the hosted services and with a temporary terminals folder.
                .AddSingleton<HybridLogicalClockState>()
                .AddSingleton<HybridLogicalClock>()
                .AddSingleton<SyncPayloadSerializer>()
                .AddSingleton<ProtectedPeerKeyStore>()
                .AddSingleton<FileTransferQueue>()
                .AddScoped<SyncDeviceService>()
                .AddSingleton<SyncFolderSettingsStore>()
                .AddSingleton<SyncFolderExchangeService>()
                .AddSingleton<CloudSyncSettingsStore>()
                .AddSingleton<ICloudHttpClientProvider, DefaultCloudHttpClientProvider>()
                .AddSingleton<CloudSyncService>()
                .AddSingleton<LocalSyncServer>()
                .AddSingleton<LanSyncMdnsAdvertiser>()
                .AddSingleton<CloudOAuthClientStore>()
                .AddSingleton<GoogleDriveSyncSettingsStore>()
                .AddSingleton<GoogleDriveSyncService>()
                .AddSingleton<CloudAutoBackupPreferencesStore>()
                .AddSingleton<CloudBackupOrchestrator>()
                .AddSingleton<BranchNetworkSyncService>()
                .AddSingleton<ZeroConfigSyncCoordinator>()
                .AddSingleton(new TerminalDeviceStore(terminals))
                .AddSingleton(new TerminalServerCertificate(terminals))
                .AddSingleton(new TerminalServerSettingsStore(terminals))
                .AddSingleton(new TerminalSessions(terminals))
                .AddSingleton<TerminalPairing>()
                .AddSingleton<TerminalServer>()
                .AddSingleton(new TerminalClientSettingsStore(terminals))
                .AddSingleton<DevicesViewModel>()
                .AddSingleton<BackupPageViewModel>()
                .AddSingleton<SyncSettingsPageViewModel>()
                .AddScoped<CatalogImportService>()
                .AddTransient<CatalogImportViewModel>();
            shop._services = services.BuildServiceProvider();
            return shop;
        }

        public IServiceScopeFactory Scopes => _services.GetRequiredService<IServiceScopeFactory>();

        public DataSyncPageViewModel Page() =>
            new("Backup & Sync", _services.GetRequiredService<BackupPageViewModel>(), _services.GetRequiredService<SyncSettingsPageViewModel>());

        public Task<Window> ShowAsync(DataSyncPageViewModel page) => ShowAsync(page, typeof(DataSyncPageViewModel));

        public async Task<Window> ShowAsync(object page, Type pageType)
        {
            var mainWindow = new MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(pageType)], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            return host;
        }

        public async Task<int> CountCatalogAsync()
        {
            using IServiceScope scope = Scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().CatalogMedicines.CountAsync();
        }

        public async ValueTask DisposeAsync() => await _services.DisposeAsync();
    }
}
