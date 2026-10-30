using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.App.Views;
using Xunit;

namespace PharmaBill.Tests;

// The top of the Retail and Wholesale dashboards: the update indicator sits where "Reports & Daily MIS" was (Reports is
// the large card on the dashboard now); quiet while there is nothing to install, a soft orange tint (never animated)
// when an update is waiting. The Sync Station shortcut is gone from the header (Sync itself stays in the sidebar and
// Settings).
public sealed class DashboardHeaderTests
{
    [Fact]
    public void UpdateButton_OnBothDashboards_ChecksAndTurnsBrightWhenAnUpdateIsWaiting()
    {
        Ui.Run(async () =>
        {
            var trace = new TraceCapture();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(trace);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            var window = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(window);
            var retailTemplate = (DataTemplate)window.Resources[new DataTemplateKey(typeof(DashboardPageViewModel))];

            foreach (Func<FrameworkElement> dashboard in new Func<FrameworkElement>[]
            {
                () => new ContentControl { Content = RuntimeHelpers.GetUninitializedObject(typeof(DashboardPageViewModel)), ContentTemplate = retailTemplate, Resources = window.Resources },
                () => new WholesaleDashboardView { DataContext = RuntimeHelpers.GetUninitializedObject(typeof(WholesaleDashboardPageViewModel)) },
            })
            {
                // As in the app: the dashboard is opened inside the main window, whose view-model is already in place.
                var shell = new DashboardHeaderShell();
                var host = new Window { DataContext = shell, Width = 1366, Height = 768 };
                Ui.Prepare(host);
                host.Show();
                host.Content = dashboard();
                await Ui.SettleAsync();

                // Quiet: "Check for Updates", no orange, no glow. Pressing it asks for a check.
                Button update = Ui.Button(host, "Check for Updates");
                BindingExpression? binding = BindingOperations.GetBindingExpression(update, ButtonBase.CommandProperty);
                Assert.True(update.Command != null, $"command not bound ({binding?.Status}): " + string.Join(" | ", trace.Lines.Where(l => l.Contains("CheckForUpdatesCommand")).Take(3)));
                Assert.Equal(false, update.Tag);
                Assert.IsNotType<LinearGradientBrush>(update.Background);
                // Quiet in every state: no glow, no animation in the template.
                Assert.Null(update.Template.FindName("Glow", update));
                Assert.Equal(shell.UpdateButtonToolTip, update.ToolTip);
                await Ui.PressAsync(update);
                Assert.Equal(1, shell.Presses);

                // An update arrives: the same button takes a soft orange tint and a download icon, and still answers.
                shell.ShowAvailable();
                await Ui.SettleAsync();
                Assert.Same(update, Ui.Button(host, "Update Available"));
                Assert.Equal(true, update.Tag);
                Assert.Equal(Color.FromRgb(0xFF, 0xF7, 0xED), ((SolidColorBrush)update.Background).Color);
                Assert.Equal(Color.FromRgb(0x9A, 0x34, 0x12), ((SolidColorBrush)update.Foreground).Color);
                Assert.Equal(0xE896, (int)((TextBlock)update.Template.FindName("Icon", update)).Text[0]);
                await Ui.PressAsync(update);
                Assert.Equal(2, shell.Presses);

                // Reports & Daily MIS is the card on the dashboard now, not a header button; it still opens Reports.
                Assert.DoesNotContain(Descendants(host).OfType<Button>(), b => AutomationProperties.GetName(b) == "Reports & Daily MIS");
                Button card = Ui.Button(host, "Reports & Daily MIS card");
                Assert.True(card.ActualWidth > 600, "the card spans the dashboard");
                Assert.Contains(Descendants(card).OfType<TextBlock>(), text => text.Text.Contains("Peak billing hours"));
                await Ui.PressAsync(card);
                Assert.Equal(["Reports"], shell.Opened);
                Assert.DoesNotContain(Descendants(host).OfType<Button>(), b => b.Content as string == "Sync Station" || (b.CommandParameter as string) == "Sync");
                host.Close();
            }
            // A real binding error on the update button (a wrong property, a failed conversion). WPF's "cannot find source"
            // for RelativeSource AncestorType=Window is not one: it is reported, process-wide, while a template is built
            // before it is in a window, and the binding resolves once it is (as asserted above). BindingErrorWatch skips it too.
            Assert.DoesNotContain(trace.Lines, l => (l.Contains("UpdateButton") || l.Contains("IsUpdateAvailable") || l.Contains("CheckForUpdatesCommand"))
                && !l.Contains("AncestorType='System.Windows.Window'", StringComparison.Ordinal));
            PresentationTraceSources.DataBindingSource.Listeners.Remove(trace);
        });
    }

    // What the main window's view-model puts on the button for each answer from the update check.
    [Fact]
    public void UpdateButtonLabel_FollowsTheUpdateCheck()
    {
        var vm = (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
        MethodInfo show = typeof(MainWindowViewModel).GetMethod("ShowUpdateButton", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Show(AppUpdateAvailability? result) => show.Invoke(vm, [result]);

        Show(null);
        Assert.Equal(("Check for Updates", false), (vm.UpdateButtonText, vm.IsUpdateAvailable));

        Show(new AppUpdateAvailability(false, "1.4.0", "1.4.0", false, UpdateChannel.MicrosoftStore, null, "Up to date ✓ (v1.4.0)"));
        Assert.Equal(("Up to date ✓", false), (vm.UpdateButtonText, vm.IsUpdateAvailable));

        // Offline, or the Store edition run outside its package: nothing was asked, so it still invites a check.
        Show(new AppUpdateAvailability(false, "1.4.0", "1.4.0", false, UpdateChannel.MicrosoftStore, "ms-windows-store://downloadsandupdates", "Updates come from the Microsoft Store", WasChecked: false));
        Assert.Equal(("Check for Updates", false), (vm.UpdateButtonText, vm.IsUpdateAvailable));
        Show(new AppUpdateAvailability(false, "1.4.0", "", false, UpdateChannel.None, null, "Could not check for updates"));
        Assert.Equal("Check for Updates", vm.UpdateButtonText);

        Show(new AppUpdateAvailability(true, "1.4.0", "1.5.0", false, UpdateChannel.MicrosoftStore, "ms-windows-store://pdp/?ProductId=X", "A new version of Pharma Remit is available in the Microsoft Store."));
        Assert.Equal(("Update Available", true), (vm.UpdateButtonText, vm.IsUpdateAvailable));
        Assert.Contains("Microsoft Store", vm.UpdateButtonToolTip);
    }

    // What each click tells the user, and where the Store edition sends them when it cannot ask the Store itself.
    [Fact]
    public void AfterCheck_SaysWhatHappened_AndOpensTheStoreOnlyForTheStoreEdition()
    {
        var upToDate = new AppUpdateAvailability(false, "1.4.0", "1.4.0", false, UpdateChannel.MicrosoftStore, "ms-windows-store://pdp/?ProductId=X", "Up to date ✓");
        var unpackaged = AppUpdateAvailability.StoreHandlesUpdates("1.4.0");
        var failed = new AppUpdateAvailability(false, "1.4.0", "1.4.0", false, UpdateChannel.None, null, "Could not check for updates (offline or unreachable). You can keep using Pharma Remit.");
        var available = new AppUpdateAvailability(true, "1.4.0", "1.5.0", false, UpdateChannel.MicrosoftStore, "ms-windows-store://pdp/?ProductId=X", "New version");

        Assert.Equal(new("Pharma Remit is up to date (version 1.4.0).", null), MainWindowViewModel.AfterCheck(Edition.Store, upToDate));
        Assert.Equal(new("Opening the Microsoft Store to check for updates…", "ms-windows-store://downloadsandupdates"), MainWindowViewModel.AfterCheck(Edition.Store, unpackaged));
        Assert.Equal(new("Opening the Microsoft Store to check for updates…", "ms-windows-store://downloadsandupdates"), MainWindowViewModel.AfterCheck(Edition.Store, failed));
        Assert.Equal("ms-windows-store://downloadsandupdates", MainWindowViewModel.AfterCheck(Edition.Store, null).OpenUri);
        Assert.Null(MainWindowViewModel.AfterCheck(Edition.Store, available).OpenUri);
        Assert.Contains("Update Available", MainWindowViewModel.AfterCheck(Edition.Store, available).Message);

        // The Local Market edition never goes to the Store; a failed check says so instead of staying silent.
        Assert.Equal(new(failed.StatusMessage, null), MainWindowViewModel.AfterCheck(Edition.LocalMarket, failed));
        Assert.Null(MainWindowViewModel.AfterCheck(Edition.LocalMarket, null).OpenUri);
        var feedUpToDate = upToDate with { Channel = UpdateChannel.Website, DownloadOrStoreUrl = null };
        Assert.Equal(new("You're on the latest version of Pharma Remit (1.4.0).", null), MainWindowViewModel.AfterCheck(Edition.LocalMarket, feedUpToDate));
        var feedAvailable = available with { Channel = UpdateChannel.Website, StatusMessage = "A newer version of Pharma Remit is available: 1.5.0" };
        Assert.Equal(new(feedAvailable.StatusMessage, null), MainWindowViewModel.AfterCheck(Edition.LocalMarket, feedAvailable));
    }

    // The real main window view-model behind the real dashboard button: a click runs a forced check, shows the result
    // in the toast, and (an update waiting) turns the button bright; the next click starts the install.
    [Fact]
    public void ClickingCheckForUpdates_RunsTheCheck_AndShowsTheResult()
    {
        Ui.Run(async () =>
        {
            var window = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(window);
            var retailTemplate = (DataTemplate)window.Resources[new DataTemplateKey(typeof(DashboardPageViewModel))];

            var updates = new FakeUpdates();
            var vm = (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
            Set(vm, "_appUpdates", updates);
            Set(vm, "_logger", Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindowViewModel>.Instance);
            Set(vm, "_confirmationService", new BackupPageEndToEndTests.AlwaysYes());
            Set(vm, "_updateButtonText", "Check for Updates");
            var opened = new List<string>();
            vm.OpenUri = opened.Add;

            var host = new Window { DataContext = vm, Width = 1366, Height = 768 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = RuntimeHelpers.GetUninitializedObject(typeof(DashboardPageViewModel)), ContentTemplate = retailTemplate, Resources = window.Resources };
            await Ui.SettleAsync();

            // Nothing answers (this build's edition reads the release feed): the click still says so.
            updates.Next = new AppUpdateAvailability(false, "1.4.0", "1.4.0", false, UpdateChannel.None, null, "You have Version 1.4.0. No update feed responded; Pharma Remit stays fully usable offline.");
            await Ui.PressAsync(Ui.Button(host, "Check for Updates"));
            Assert.Equal([true], updates.Forced);
            Assert.Equal(AppEdition.Current == Edition.Store ? "Opening the Microsoft Store to check for updates…" : updates.Next.StatusMessage, vm.BarcodeToastMessage);
            Assert.Equal(AppEdition.Current == Edition.Store ? ["ms-windows-store://downloadsandupdates"] : [], opened);

            // An update is found: the button turns bright, and the next click starts the install.
            updates.Next = new AppUpdateAvailability(true, "1.4.0", "1.5.0", false, UpdateChannel.MicrosoftStore, "ms-windows-store://pdp/?ProductId=X", "A new version of Pharma Remit is available in the Microsoft Store.");
            await Ui.PressAsync(Ui.Button(host, "Check for Updates"));
            Button bright = Ui.Button(host, "Update Available");
            Assert.Equal(true, bright.Tag);
            Assert.Contains(AppEdition.Current == Edition.Store ? "Update Available" : "is available", vm.BarcodeToastMessage);
            await Ui.PressAsync(bright);
            Assert.Equal(1, updates.Launches);
            Assert.Equal([true, true], updates.Forced);
            host.Close();
        });
    }

    // Local Market edition: the website has a newer version. The indicator shows it quietly; a click opens the card with
    // version, date, notes and size; its one button opens the download page in the browser (nothing is downloaded here);
    // the x hides it until a later version.
    [Fact]
    public void WebsiteUpdate_ShowsItsDetails_OpensTheDownloadPage_AndCanBeHidden()
    {
        Ui.Run(async () =>
        {
            var window = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(window);
            var retailTemplate = (DataTemplate)window.Resources[new DataTemplateKey(typeof(DashboardPageViewModel))];

            var updates = new FakeUpdates();
            var vm = (MainWindowViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainWindowViewModel));
            Set(vm, "_appUpdates", updates);
            Set(vm, "_logger", Microsoft.Extensions.Logging.Abstractions.NullLogger<MainWindowViewModel>.Instance);
            Set(vm, "_confirmationService", new BackupPageEndToEndTests.AlwaysYes());
            Set(vm, "_updateButtonText", "Check for Updates");
            var opened = new List<string>();
            vm.OpenUri = opened.Add;

            var host = new Window { DataContext = vm, Width = 1366, Height = 768 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = RuntimeHelpers.GetUninitializedObject(typeof(DashboardPageViewModel)), ContentTemplate = retailTemplate, Resources = window.Resources };
            await Ui.SettleAsync();

            var release = new PublishedRelease("1.0.13", new DateOnly(2026, 11, 5), "https://pharmaremit.com/download/PharmaRemit_Setup_1.0.13.0.exe",
                79_253_601, new string('a', 64), "1.0.10", ["Faster stock search", "GSTR-1 includes credit notes"]);
            updates.Next = new AppUpdateAvailability(true, "1.0.12.0", "1.0.13", false, UpdateChannel.Website, release.DownloadUrl,
                "A newer version of Pharma Remit is available: 1.0.13, released 5 Nov 2026 (75.6 MB download).", Release: release);

            // The background check finds it: the label changes, nothing opens by itself.
            await vm.CheckForAppUpdatesAsync();
            await Ui.SettleAsync();
            Button indicator = Ui.Button(host, "Update available · 1.0.13");
            Assert.False(vm.IsUpdateDetailsOpen);
            Assert.Empty(opened);

            // A click shows the details.
            await Ui.PressAsync(indicator);
            await Ui.SettleAsync();
            Assert.True(vm.IsUpdateDetailsOpen);
            Popup card = Descendants(host).OfType<Popup>().Single(popup => popup.IsOpen);
            string[] texts = Descendants(card.Child).OfType<TextBlock>().Select(text => text.Text).ToArray();
            Assert.Contains("Pharma Remit 1.0.13", texts);
            Assert.Contains("Released 5 Nov 2026 · 75.6 MB download", texts);
            Assert.Contains("Faster stock search", texts);
            Assert.Contains("GSTR-1 includes credit notes", texts);

            // Its one button opens the download page in the browser.
            Button download = Descendants(card.Child).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Open download page");
            await Ui.PressAsync(download);
            Assert.Equal([release.DownloadUrl], opened);
            Assert.False(vm.IsUpdateDetailsOpen);
            Assert.Equal(0, updates.Launches);

            // The x hides it until a later version.
            await Ui.PressAsync(Ui.Named<Button>(host, "Hide update notice"));
            Assert.Equal(["1.0.13"], updates.Dismissed);
            Assert.False(vm.IsUpdateAvailable);
            Assert.NotNull(Ui.Button(host, "Check for Updates"));
            host.Close();
        });
    }

    private static void Set(object target, string field, object value) =>
        typeof(MainWindowViewModel).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class FakeUpdates : IAppUpdateService
    {
        public AppUpdateAvailability? Next { get; set; }
        public List<bool> Forced { get; } = [];
        public int Launches { get; private set; }
        public AppUpdateAvailability? LastResult { get; private set; }
        public event EventHandler<AppUpdateAvailability>? UpdateStateChanged;

        public Task<AppUpdateAvailability> CheckForUpdatesAsync(bool force = false, CancellationToken cancellationToken = default)
        {
            Forced.Add(force);
            LastResult = Next!;
            UpdateStateChanged?.Invoke(this, LastResult);
            return Task.FromResult(LastResult);
        }

        public Task LaunchUpdateAsync(CancellationToken cancellationToken = default)
        {
            Launches++;
            return Task.CompletedTask;
        }

        public List<string> Dismissed { get; } = [];

        public void Dismiss(string version) => Dismissed.Add(version);

        public bool IsDismissed(string version) => Dismissed.Contains(version);
    }

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
}

// Stands in for the main window's view-model: the dashboard header binds to its NavigateCommand and update button.
public sealed class DashboardHeaderShell : ObservableObject
{
    private string _updateButtonText = "Check for Updates";
    private bool _isUpdateAvailable;
    private string _updateButtonToolTip = "Ask the Microsoft Store whether a newer version of Pharma Remit is available";

    public DashboardHeaderShell()
    {
        NavigateCommand = new RelayCommand<string>(key => Opened.Add(key!));
        CheckForUpdatesCommand = new RelayCommand(() => Presses++);
    }

    public List<string> Opened { get; } = [];

    public int Presses { get; private set; }

    public System.Windows.Input.ICommand NavigateCommand { get; }

    public System.Windows.Input.ICommand CheckForUpdatesCommand { get; }

    public string UpdateButtonText { get => _updateButtonText; private set => SetProperty(ref _updateButtonText, value); }

    public bool IsUpdateAvailable { get => _isUpdateAvailable; private set => SetProperty(ref _isUpdateAvailable, value); }

    public string UpdateButtonToolTip { get => _updateButtonToolTip; private set => SetProperty(ref _updateButtonToolTip, value); }

    // The rest of what the update indicator binds to, as the main window's view-model has it.
    public bool CanDismissUpdate => false;

    public bool IsUpdateDetailsOpen { get; set; }

    public PublishedRelease? AvailableRelease => null;

    public System.Windows.Input.ICommand DismissUpdateCommand { get; } = new RelayCommand(() => { });

    public System.Windows.Input.ICommand OpenUpdateDownloadCommand { get; } = new RelayCommand(() => { });

    public void ShowAvailable()
    {
        UpdateButtonText = "Update Available";
        UpdateButtonToolTip = "A new version of Pharma Remit is available in the Microsoft Store.";
        IsUpdateAvailable = true;
    }
}

public sealed class TraceCapture : TraceListener
{
    public List<string> Lines { get; } = [];

    public override void Write(string? message) { }

    public override void WriteLine(string? message)
    {
        if (message != null)
        {
            Lines.Add(message);
        }
    }
}
