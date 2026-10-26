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
// real Backup and Sync pages. The old "Backup" and "Sync" keys (Settings links, reminders, the assistant) open their tab.
public sealed class DataSyncTests
{
    [Fact]
    public void OldScreenKeys_OpenTheirTab_AndOnTheScreenOnlyTheTabChanges()
    {
        WithShop(async shop =>
        {
            DataSyncPageViewModel page = shop.Page();
            var navigation = new NavigationService();
            var routes = new MergedScreenRoutes(navigation);
            object? current = null;
            var built = new List<string>();
            ComplianceTab? complianceTab = null;
            // What the main window does when the section changes: old keys are routed, anything else builds its screen.
            navigation.SectionChanged += (_, _) =>
            {
                string key = navigation.CurrentSectionKey;
                if (routes.ConsumeKeepCurrentPage() || routes.TryOpen(key, current))
                {
                    return;
                }
                built.Add(key);
                if (key == DataSyncPageViewModel.SectionKey && routes.TakeDataSyncTab() is DataSyncTab tab)
                {
                    page.ShowTab(tab);
                }
                if (key == ComplianceRegistersPageViewModel.SectionKey)
                {
                    complianceTab = routes.TakeComplianceTab();
                }
                current = key == DataSyncPageViewModel.SectionKey ? page : key;
            };

            // Settings' "Open Sync Station" asks for "Sync" directly.
            navigation.Navigate("Sync");
            Assert.Equal(["DataSync"], built);
            Assert.Equal("DataSync", navigation.CurrentSectionKey);
            Assert.True(page.IsSyncTab);

            // The USB reminder's "Back up now" asks for "Backup" while the screen is open: only the tab changes.
            navigation.Navigate("Backup");
            await Ui.SettleAsync();
            Assert.Equal(["DataSync"], built);
            Assert.Equal("DataSync", navigation.CurrentSectionKey);
            Assert.True(page.IsBackupTab);

            // Leaving and coming back by the sidebar keeps the tab last used.
            page.ShowTab(DataSyncTab.Sync);
            navigation.Navigate("Dashboard");
            navigation.Navigate("DataSync");
            Assert.True(page.IsSyncTab);

            // The registers' old keys work the same way.
            navigation.Navigate("DrugRecords");
            Assert.Equal("Compliance", navigation.CurrentSectionKey);
            Assert.Equal(ComplianceTab.DrugRecords, complianceTab);
            Assert.False(routes.TryOpen("Reports", current));

            // The assistant still answers with the old keys.
            var today = new DateOnly(2026, 10, 21);
            Assert.Equal(DataSyncTab.Backup, DataSyncPageViewModel.TabForSection(AssistantQueryInterpreter.Interpret("backup", today).SectionKey));
            Assert.Equal(DataSyncTab.Sync, DataSyncPageViewModel.TabForSection(AssistantQueryInterpreter.Interpret("sync", today).SectionKey));
        });
    }

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
                .AddSingleton<SyncSettingsPageViewModel>();
            shop._services = services.BuildServiceProvider();
            return shop;
        }

        public DataSyncPageViewModel Page() =>
            new("Backup & Sync", _services.GetRequiredService<BackupPageViewModel>(), _services.GetRequiredService<SyncSettingsPageViewModel>());

        public async Task<Window> ShowAsync(DataSyncPageViewModel page)
        {
            var mainWindow = new MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(DataSyncPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            return host;
        }

        public async ValueTask DisposeAsync() => await _services.DisposeAsync();
    }
}
