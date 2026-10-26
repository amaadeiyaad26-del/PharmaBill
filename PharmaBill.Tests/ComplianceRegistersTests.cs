using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Compliance & Registers: the statutory registers and the drug records movement report as two tabs of one screen.
// Each tab still asks for the PIN (and audits it) the first time it is opened; switching back keeps its filters.
public sealed class ComplianceRegistersTests
{
    private const string Pin = "2468";

    [Fact]
    public void OldScreenKeys_OpenTheirTab()
    {
        Assert.Equal(ComplianceTab.StatutoryRegisters, ComplianceRegistersPageViewModel.TabForSection("Registers"));
        Assert.Equal(ComplianceTab.DrugRecords, ComplianceRegistersPageViewModel.TabForSection("DrugRecords"));
        Assert.Null(ComplianceRegistersPageViewModel.TabForSection("Compliance"));
        // The assistant still asks for "Registers", which now opens the registers tab.
        Assert.Equal("Registers", AssistantQueryInterpreter.Interpret("open schedule h1 register", new DateOnly(2026, 10, 21)).SectionKey);
    }

    [Fact]
    public void Tabs_EachUnlockOnce_KeepTheirFilters_AndShowTheOriginalTools()
    {
        Ui.Run(async () =>
        {
            await using Compliance shop = await Compliance.OpenAsync();
            ComplianceRegistersPageViewModel page = shop.Page();
            using var pin = new PinAnswerer(Pin);
            Window host = await shop.ShowAsync(page);
            await page.LoadAsync();
            await Ui.SettleAsync();

            // One title, the two tabs right under it, and the registers' own tools (date range, register type, exports).
            Assert.Equal(["Enter your PIN or password to open statutory registers (attempt 1 of 3)."], pin.Prompts);
            Assert.True(page.Registers.IsUnlocked);
            Assert.Single(Texts(host), text => text == "Compliance & Registers");
            Assert.DoesNotContain("Statutory registers", Texts(host));
            Assert.True(Equals(true, Ui.Button(host, "Statutory Registers").Tag));
            Assert.True(Equals(false, Ui.Button(host, "Drug Records Movement").Tag));
            Ui.Button(host, "📄 Export to PDF");
            Ui.Button(host, "📊 Export to Excel (.xlsx)");
            Assert.Equal(2, Descendants(host).OfType<DatePicker>().Count(picker => picker.IsVisible));
            ComboBox registerType = Ui.Single<ComboBox>(host, combo => combo.ItemsSource == page.Registers.RegisterTypes);
            registerType.SelectedItem = "NDPS";

            // Drug Records Movement asks for the PIN for itself, and shows its search and Run button.
            await Ui.PressAsync(Ui.Button(host, "Drug Records Movement"));
            await Ui.UntilAsync(() => page.DrugRecords.IsUnlocked, "the drug records tab to unlock");
            Assert.Equal(2, pin.Prompts.Count);
            Assert.Contains("open Drug Records", pin.Prompts[1]);
            Assert.True(page.IsDrugRecordsTab);
            Assert.DoesNotContain("Drug Records", Texts(host));
            Assert.Contains("Run Drug Records", Texts(host));
            Ui.Button(host, "Find brand or salt");
            page.DrugRecords.SearchText = "Paracetamol";

            // Back to the registers: no second PIN, and the filter chosen before is still there.
            await Ui.PressAsync(Ui.Button(host, "Statutory Registers"));
            Assert.Equal(2, pin.Prompts.Count);
            Assert.Equal("NDPS", page.Registers.SelectedRegisterType);
            Assert.Equal("NDPS", Ui.Single<ComboBox>(host, combo => combo.ItemsSource == page.Registers.RegisterTypes).SelectedItem);
            await Ui.PressAsync(Ui.Button(host, "Drug Records Movement"));
            Assert.Equal(2, pin.Prompts.Count);
            Assert.Equal("Paracetamol", page.DrugRecords.SearchText);

            // Both openings are in the audit trail, each naming what was opened.
            List<string?> granted = await shop.Database.Context.AuditLogs.AsNoTracking()
                .Where(log => log.Action == "SensitiveRecordsAccessGranted").Select(log => log.Details).ToListAsync();
            Assert.Equal(["open statutory registers", "open Drug Records"], granted.Order().Reverse().ToList());
            host.Close();
        });
    }

    [Fact]
    public void CancelledPin_KeepsTheTabLocked_AndChoosingItAgainAsksAgain()
    {
        Ui.Run(async () =>
        {
            await using Compliance shop = await Compliance.OpenAsync();
            ComplianceRegistersPageViewModel page = shop.Page(ComplianceTab.DrugRecords);
            Window host = await shop.ShowAsync(page);
            using (new PinAnswerer(null))
            {
                await page.LoadAsync();
            }
            await Ui.SettleAsync();
            Assert.True(page.IsDrugRecordsTab);
            Assert.False(page.DrugRecords.IsUnlocked);
            Assert.Contains("Access cancelled or PIN verification failed.", Texts(host));

            using var pin = new PinAnswerer(Pin);
            await Ui.PressAsync(Ui.Button(host, "Drug Records Movement"));
            await Ui.UntilAsync(() => page.DrugRecords.IsUnlocked, "the drug records tab to unlock");
            Assert.Single(pin.Prompts);
            host.Close();
        });
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

    // Answers the real PIN dialog as a person would: types the PIN and presses Continue, or presses Cancel (null).
    private sealed class PinAnswerer : IDisposable
    {
        private readonly DispatcherTimer _timer;

        public PinAnswerer(string? pin)
        {
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, (_, _) =>
            {
                foreach (PinPromptWindow prompt in Application.Current.Windows.OfType<PinPromptWindow>().Where(window => window.IsLoaded && window.IsVisible).ToList())
                {
                    Prompts.Add(prompt.Prompt);
                    Button answer;
                    if (pin == null)
                    {
                        answer = Ui.Button(prompt, "Cancel");
                    }
                    else
                    {
                        ((PasswordBox)prompt.FindName("PinBox")).Password = pin;
                        answer = Ui.Button(prompt, "Continue");
                    }
                    ((IInvokeProvider)new ButtonAutomationPeer(answer).GetPattern(PatternInterface.Invoke)).Invoke();
                }
            }, Dispatcher.CurrentDispatcher);
            _timer.Start();
        }

        public List<string> Prompts { get; } = [];

        public void Dispose() => _timer.Stop();
    }

    private sealed class Compliance : IAsyncDisposable
    {
        private ServiceProvider _services = null!;
        private Window? _previousMainWindow;

        private Compliance(DatabaseTestContext database) => Database = database;

        public DatabaseTestContext Database { get; }
        public CurrentSession Session { get; } = new();

        public static async Task<Compliance> OpenAsync()
        {
            var database = await DatabaseTestContext.CreateAsync();
            var shop = new Compliance(database);
            E2EHealthSeed.Catalog catalog = await E2EHealthSeed.SeedAsync(database.Context);
            await database.Context.Database.ExecuteSqlRawAsync("UPDATE AppUsers SET PasswordHash = {0} WHERE Id = {1}", PasswordHasher.Hash(Pin), catalog.Admin.Id);
            shop._services = new ServiceCollection()
                .AddScoped(_ => database.OpenContext())
                .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
                .BuildServiceProvider();
            shop.Session.SignIn(catalog.Admin);
            return shop;
        }

        public ComplianceRegistersPageViewModel Page(ComplianceTab tab = ComplianceTab.StatutoryRegisters)
        {
            IServiceScopeFactory scopes = _services.GetRequiredService<IServiceScopeFactory>();
            var access = new SensitiveAccessService(scopes, Session, new DocumentOutputSettingsStore(Path.Combine(Database.Storage.RootDirectory, "print")));
            var export = new TabularExportService();
            return new ComplianceRegistersPageViewModel("Compliance & Registers",
                new StatutoryRegistersPageViewModel(new StatutoryRegisterService(scopes), access, null!, null!, export, Session) { ShowPageTitle = false },
                new DrugRecordsPageViewModel(new DrugRecordsService(scopes), null!, export, access, null!, Session) { ShowPageTitle = false },
                tab);
        }

        public async Task<Window> ShowAsync(ComplianceRegistersPageViewModel page)
        {
            var mainWindow = new MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(ComplianceRegistersPageViewModel))], Resources = mainWindow.Resources };
            // The PIN dialog is owned by the app's main window, as on the real screen.
            _previousMainWindow = Application.Current.MainWindow;
            Application.Current.MainWindow = host;
            await Ui.SettleAsync();
            return host;
        }

        public async ValueTask DisposeAsync()
        {
            Application.Current.MainWindow = _previousMainWindow;
            await _services.DisposeAsync();
            await Database.DisposeAsync();
        }
    }
}
