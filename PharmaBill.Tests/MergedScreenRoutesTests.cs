using System.Runtime.CompilerServices;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Ai;
using Xunit;

namespace PharmaBill.Tests;

// The sidebar keys of screens that became tabs (Stock, Stock in hand, Stock transfer, Accounts, the customer and
// supplier lists, Registers, Drug Records, Backup, Sync) still open the right place, whether asked for from the
// sidebar code or directly through the navigation service (as Settings, dashboards and reminders do).
public sealed class MergedScreenRoutesTests
{
    [Fact]
    public void OldKeys_OpenTheirHubTab_AndOnTheHubOnlyTheTabChanges()
    {
        Ui.Run(async () =>
        {
            var shell = new Shell();
            HubPageViewModel stock = shell.AddHub("StockHub", "inventory", "inhand", "transfer");
            HubPageViewModel parties = shell.AddHub("Parties", "ledgers", "Customers", "suppliers");
            shell.Routes.AddHubAlias("Stock", "StockHub", "inventory");
            shell.Routes.AddHubAlias("StockInHand", "StockHub", "inhand");
            shell.Routes.AddHubAlias("StockTransfer", "StockHub", "transfer");
            shell.Routes.AddHubAlias("Accounts", "Parties", "ledgers");
            shell.Routes.AddHubAlias("Customers", "Parties", "Customers");
            shell.Routes.AddHubAlias("WholesaleSuppliers", "Parties", "suppliers");

            // A dashboard drill-down asks for "StockTransfer": Stock Management opens on that tab.
            shell.Navigation.Navigate("StockTransfer");
            Assert.Equal(["StockHub"], shell.Built);
            Assert.Equal("StockHub", shell.Navigation.CurrentSectionKey);
            Assert.Equal("transfer", stock.ActiveTabKey);

            // Already there, "Stock" only switches the tab: nothing is rebuilt.
            shell.Navigation.Navigate("Stock");
            await Ui.SettleAsync();
            Assert.Equal(["StockHub"], shell.Built);
            Assert.Equal("StockHub", shell.Navigation.CurrentSectionKey);
            Assert.Equal("inventory", stock.ActiveTabKey);

            // The sidebar item itself keeps the tab last used.
            stock.ShowTab("inhand");
            shell.Navigation.Navigate("Dashboard");
            shell.Navigation.Navigate("StockHub");
            Assert.Equal("inhand", stock.ActiveTabKey);

            // Customers, suppliers and ledgers are tabs of Accounts & Parties.
            shell.Navigation.Navigate("WholesaleSuppliers");
            Assert.Equal(("Parties", "suppliers"), (shell.Navigation.CurrentSectionKey, parties.ActiveTabKey));
            shell.Navigation.Navigate("Accounts");
            Assert.Equal(("Parties", "ledgers"), (shell.Navigation.CurrentSectionKey, parties.ActiveTabKey));
            shell.Navigation.Navigate("Customers");
            Assert.Equal("Customers", parties.ActiveTabKey);

            Assert.False(shell.Routes.TryOpen("Reports", shell.Current));
        });
    }

    [Fact]
    public void BackupAndSync_OpenInSettings_AndRegistersOpenTheirTab()
    {
        Ui.Run(async () =>
        {
            var shell = new Shell();
            var shown = new List<(DataSyncTab? Tab, bool OnSettings)>();
            shell.Routes.ShowDataSyncInSettings = (tab, onSettings) =>
            {
                shown.Add((tab, onSettings));
                return Task.CompletedTask;
            };

            // Settings' search result "Sync Station" asks for "Sync": Settings opens on Backup & Sync, Sync tab.
            shell.Navigation.Navigate("Sync");
            Assert.Equal("Settings", shell.Navigation.CurrentSectionKey);
            Assert.Equal([(DataSyncTab.Sync, false)], shown);
            Assert.Equal(["Settings"], shell.Built);

            // On Settings, the USB reminder's "Back up now" asks for "Backup": the tab changes and loads there.
            shell.Navigation.Navigate("Backup");
            await Ui.SettleAsync();
            Assert.Equal("Settings", shell.Navigation.CurrentSectionKey);
            Assert.Equal((DataSyncTab.Backup, true), shown[^1]);
            Assert.Equal(["Settings"], shell.Built);

            shell.Navigation.Navigate("DrugRecords");
            Assert.Equal("Compliance", shell.Navigation.CurrentSectionKey);
            Assert.Equal(ComplianceTab.DrugRecords, shell.ComplianceTab);

            // The assistant still answers with the old keys.
            var today = new DateOnly(2026, 10, 21);
            Assert.Equal(DataSyncTab.Backup, DataSyncPageViewModel.TabForSection(AssistantQueryInterpreter.Interpret("backup", today).SectionKey));
            Assert.Equal(DataSyncTab.Sync, DataSyncPageViewModel.TabForSection(AssistantQueryInterpreter.Interpret("sync", today).SectionKey));
        });
    }

    // What the main window does when the section changes: old keys are routed, anything else builds its screen.
    private sealed class Shell
    {
        private readonly Dictionary<string, HubPageViewModel> _hubs = [];

        public Shell()
        {
            Routes = new MergedScreenRoutes(Navigation);
            Navigation.SectionChanged += (_, _) =>
            {
                string key = Navigation.CurrentSectionKey;
                if (Routes.ConsumeKeepCurrentPage() || Routes.TryOpen(key, Current))
                {
                    return;
                }
                Built.Add(key);
                if (_hubs.TryGetValue(key, out HubPageViewModel? hub))
                {
                    if (Routes.TakeHubTab(key) is string tab)
                    {
                        hub.ShowTab(tab);
                    }
                    Current = hub;
                    return;
                }
                if (key == ComplianceRegistersPageViewModel.SectionKey)
                {
                    ComplianceTab = Routes.TakeComplianceTab();
                }
                // Settings only needs to be recognised as Settings here.
                Current = key == MergedScreenRoutes.SettingsSectionKey ? RuntimeHelpers.GetUninitializedObject(typeof(SettingsPageViewModel)) : key;
            };
        }

        public NavigationService Navigation { get; } = new();
        public MergedScreenRoutes Routes { get; }
        public List<string> Built { get; } = [];
        public object? Current { get; private set; }
        public ComplianceTab? ComplianceTab { get; private set; }

        public HubPageViewModel AddHub(string key, params string[] tabs)
        {
            var hub = new HubPageViewModel(key, key, string.Empty);
            hub.SetTabs(tabs.Select(tab => new HubTab(tab, tab, new object())));
            _hubs[key] = hub;
            return hub;
        }
    }
}
