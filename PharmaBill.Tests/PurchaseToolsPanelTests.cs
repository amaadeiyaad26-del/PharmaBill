using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// The invoice tools down the right of the Purchases screen: every tool a real button with its icon, name and shortcut;
// greyed with the reason in words when it cannot run; an icon strip when narrow or collapsed; the shortcuts unchanged.
public sealed class PurchaseToolsPanelTests
{
    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static readonly (string Name, string Label, string Shortcut)[] Tools =
    [
        ("Extract from invoice", "Extract from Invoice (PDF/Image)", "Ctrl+Shift+E"),
        ("Scan with webcam", "Scan with Webcam", "F7"),
        ("Scan purchase invoice with handheld scanner", "Handheld / USB Scanner", "Ctrl+Alt+S"),
        ("Scan via phone app", "Scan via Phone App", "Ctrl+Shift+P"),
        ("Load CSV or Excel", "Load CSV / Excel", "Ctrl+Shift+L"),
        ("Attach invoice file", "Attach file", "Ctrl+Shift+A"),
        ("Invoices history", "Invoices History", "Ctrl+Shift+H"),
    ];

    [Fact]
    public void InvoiceTools_AreAlwaysInView_SayWhyWhenTheyCannotRun_AndKeepTheirShortcuts()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            var allowed = new Allowed();
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Storage)
                .AddSingleton(database.BranchSettings)
                .AddSingleton<IEntitlementService>(allowed)
                .AddSingleton<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>()
                .AddScoped(_ => database.OpenContext())
                .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
                .AddScoped<NumberSeriesService>()
                .AddScoped<StorageLocationService>()
                .AddScoped<BranchService>()
                .AddScoped<PurchaseService>()
                .AddScoped<AddStockService>()
                .BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(catalog.Admin);
            var page = new PurchasePageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, null!, new BackupPageEndToEndTests.AlwaysYes(),
                null!, null!, null!, null!, null!, null!, null!);
            bool scannerConnected = false;
            page.ScannerProbe = () => scannerConnected;
            page.CameraProbe = () => Task.FromResult(true);
            await page.LoadAsync();
            await page.DeviceCheck;

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1600, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(PurchasePageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();

            // A wide screen: the panel is open, every tool a button with its name and shortcut, and the dropdown is gone.
            Assert.True(page.ScreenWidth > PurchasePageViewModel.ToolsPanelCompactBelow, "screen width " + page.ScreenWidth);
            Border panel = Ui.Named<Border>(host, "Invoice tools");
            Assert.Equal(256, panel.ActualWidth);
            List<string> texts = Texts(panel);
            Assert.Contains("Invoice tools", texts);
            Assert.Contains("Capture the bill", texts);
            Assert.Contains("Import", texts);
            Assert.Contains("History", texts);
            foreach ((string name, string label, string shortcut) in Tools)
            {
                Button button = Ui.Named<Button>(host, name);
                Assert.True(button.ActualHeight >= 44, name + " is big enough to hit");
                List<string> inside = Texts(button);
                Assert.Contains(label, inside);
                Assert.Contains(shortcut, inside);
            }
            Assert.DoesNotContain(Texts(host), text => text.Contains("Invoice Tools ▾", StringComparison.Ordinal));
            Assert.Same(page.ExtractInvoicesCommand, Ui.Named<Button>(host, "Extract from invoice").Command);
            Assert.Same(page.ShowInvoiceHistoryCommand, Ui.Named<Button>(host, "Invoices history").Command);

            // The add-item row keeps the whole width, as before; the panel sits beside the purchase lines, not over them.
            Button addToBill = Ui.Button(host, "Add to bill");
            Assert.True(addToBill.TranslatePoint(new Point(addToBill.ActualWidth, 0), host).X > panel.TranslatePoint(new Point(0, 0), host).X,
                "the add-item row runs over the panel's column, as wide as before");
            Assert.True(addToBill.TranslatePoint(new Point(0, 0), panel).Y < 0, "the panel starts below the add-item row");
            DataGrid lines = Descendants(host).OfType<DataGrid>().First(grid => grid.ItemsSource == page.PagedLines || grid.ItemsSource == page.Lines);
            Assert.True(lines.ActualWidth >= 640, "purchase grid width " + lines.ActualWidth);
            Assert.True(lines.TranslatePoint(new Point(lines.ActualWidth, 0), host).X <= panel.TranslatePoint(new Point(0, 0), host).X);

            // No scanner connected: greyed, and the reason is written under it. Check devices finds it once plugged in.
            Button scanner = Ui.Named<Button>(host, "Scan purchase invoice with handheld scanner");
            Assert.False(scanner.IsEnabled);
            Assert.Contains("No handheld or USB scanner is connected. Plug one in, then Check devices.", Texts(panel));
            Assert.True(Ui.Named<Button>(host, "Scan with webcam").IsEnabled);
            scannerConnected = true;
            await Ui.PressAsync(Ui.Named<Button>(host, "Check devices"));
            Assert.True(scanner.IsEnabled);
            Assert.DoesNotContain(Texts(panel), text => text.StartsWith("No handheld", StringComparison.Ordinal));
            Assert.False(Ui.Named<Button>(host, "Check devices", visibleOnly: false).IsVisible);

            // While an invoice is being read, the capture and import tools wait, and say so; history stays open.
            page.IsImporting = true;
            await Ui.SettleAsync();
            Assert.False(Ui.Named<Button>(host, "Extract from invoice").IsEnabled);
            Assert.False(Ui.Named<Button>(host, "Load CSV or Excel").IsEnabled);
            Assert.True(Ui.Named<Button>(host, "Invoices history").IsEnabled);
            Assert.Contains("An invoice is being read. Wait for it to finish.", Texts(panel));
            page.IsImporting = false;
            await Ui.SettleAsync();
            Assert.True(Ui.Named<Button>(host, "Extract from invoice").IsEnabled);

            // The shortcuts are the screen's own, unchanged.
            Grid screen = Descendants(host).OfType<Grid>().First(grid => grid.InputBindings.OfType<KeyBinding>().Any(binding => binding.Key == Key.F7));
            void Shortcut(Key key, ModifierKeys modifiers, ICommand command) =>
                Assert.Contains(screen.InputBindings.OfType<KeyBinding>(), binding => binding.Key == key && binding.Modifiers == modifiers && ReferenceEquals(binding.Command, command));
            Shortcut(Key.E, ModifierKeys.Control | ModifierKeys.Shift, page.ExtractInvoicesCommand);
            Shortcut(Key.F7, ModifierKeys.None, page.ScanInvoiceCommand);
            Shortcut(Key.S, ModifierKeys.Control | ModifierKeys.Alt, page.ScanPurchaseWithHandheldScannerCommand);
            Shortcut(Key.P, ModifierKeys.Control | ModifierKeys.Shift, page.ScanViaPhoneCommand);
            Shortcut(Key.L, ModifierKeys.Control | ModifierKeys.Shift, page.LoadSpreadsheetCommand);
            Shortcut(Key.A, ModifierKeys.Control | ModifierKeys.Shift, page.AttachInvoiceFileCommand);
            Shortcut(Key.H, ModifierKeys.Control | ModifierKeys.Shift, page.ShowInvoiceHistoryCommand);

            // Collapsed by the user: an icon strip with short names; open again.
            await Ui.PressAsync(Ui.Named<Button>(host, "Collapse or expand invoice tools"));
            Assert.Equal(84, panel.ActualWidth);
            Assert.Contains("Scanner", Texts(panel));
            Assert.DoesNotContain("Handheld / USB Scanner", Texts(panel));
            await Ui.PressAsync(Ui.Named<Button>(host, "Collapse or expand invoice tools"));
            Assert.Equal(256, panel.ActualWidth);

            // A small window: an icon strip by itself, so the purchase keeps its width.
            var narrowPage = new PurchasePageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, null!, new BackupPageEndToEndTests.AlwaysYes(),
                null!, null!, null!, null!, null!, null!, null!) { ScannerProbe = () => true, CameraProbe = () => Task.FromResult(true) };
            var narrow = new Window { Width = 1050, Height = 900 };
            Ui.Prepare(narrow);
            narrow.Show();
            narrow.Content = new ContentControl { Content = narrowPage, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(PurchasePageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            Assert.True(narrowPage.IsToolsPanelCompact, "compact at " + narrowPage.ScreenWidth);
            Assert.Equal(84, Ui.Named<Border>(narrow, "Invoice tools").ActualWidth);
            bindings.AssertNone();
            narrow.Close();
            host.Close();
        });
    }

    private static List<string> Texts(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>().Where(text => text.IsVisible && !string.IsNullOrEmpty(text.Text)).Select(text => text.Text).ToList();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            foreach (DependencyObject child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }
}
