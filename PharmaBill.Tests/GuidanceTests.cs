using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// The guidance a new shop sees: the getting-started checklist on the dashboard, and the GST screen's worked sum.
public sealed class GuidanceTests
{
    [Fact]
    public async Task Checklist_TicksEachMilestoneFromTheShopsRecords()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var progress = new OnboardingProgressService(database.Context);
        Assert.Equal(new OnboardingProgress(false, false, false), await progress.GetAsync());

        database.Context.PharmacyProfiles.Add(new PharmacyProfile { Name = "New Pharmacy", Address = "Residency Road, Srinagar", Gstin = "01ABCDE1234F1Z5", BusinessMode = BusinessMode.Retail, InvoicePrefix = "NEW" });
        await database.Context.SaveChangesAsync();
        Assert.Equal(new OnboardingProgress(true, false, false), await progress.GetAsync());

        await using var seeded = await DatabaseTestContext.CreateAsync();
        var catalog = await E2EHealthSeed.SeedAsync(seeded.Context);
        Assert.Equal(new OnboardingProgress(true, true, false), await new OnboardingProgressService(seeded.Context).GetAsync());
        await seeded.CreateRetailBillingService(new Allowed()).SaveSaleAsync(
            new SaveRetailSaleInput("Walk-in", "9876543210", null, null, null, null,
                [new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 1m, 0m, 30m)], [new RetailPaymentInput("Cash", 30m)], false),
            catalog.Admin.Id, UserRole.Pharmacist);
        OnboardingProgress done = await new OnboardingProgressService(seeded.Context).GetAsync();
        Assert.True(done.Complete);
    }

    [Fact]
    public void Dashboard_ShowsTheChecklist_ItsStepsOpenTheRightScreens_AndHideIsRemembered()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            database.Context.PharmacyProfiles.Add(new PharmacyProfile { Name = "New Pharmacy", Address = "Residency Road, Srinagar", Gstin = "01ABCDE1234F1Z5", BusinessMode = BusinessMode.Retail, InvoicePrefix = "NEW" });
            await database.Context.SaveChangesAsync();
            ServiceProvider services = new ServiceCollection().AddSingleton(database.Context).BuildServiceProvider();
            var settings = new OnboardingSettingsStore(database.Storage.RootDirectory);
            DashboardPageViewModel dashboard = await LoadedDashboardAsync(services, settings);

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var shell = new DashboardHeaderShell();
            var host = new Window { DataContext = shell, Width = 1366, Height = 900 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = dashboard, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(DashboardPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();

            Assert.True(Ui.Named<Border>(host, "Getting started").IsVisible);
            Assert.Equal("1 of 3 done", dashboard.GettingStartedProgress);
            // Store details are done, so only the two open steps offer a button.
            Assert.Empty(Descendants(host).OfType<Button>().Where(b => b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b) == "Open settings"));
            await Ui.PressAsync(Ui.Button(host, "Enter a purchase"));
            await Ui.PressAsync(Ui.Button(host, "Start a bill"));
            Assert.Equal(["Purchases", "Billing"], shell.Opened);

            await Ui.PressAsync(Ui.Button(host, "Hide"));
            Assert.False(Ui.Named<Border>(host, "Getting started", visibleOnly: false).IsVisible);
            Assert.False((await LoadedDashboardAsync(services, settings)).ShowGettingStarted);
            host.Close();
        });
    }

    [Fact]
    public void GstScreen_ShowsThePeriodsWorkedSum()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            await database.CreateRetailBillingService(new Allowed()).SaveSaleAsync(
                new SaveRetailSaleInput("Walk-in", "9876543210", null, null, null, null,
                    [new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 2m, 0m, 30m)], [new RetailPaymentInput("Cash", 60m)], false),
                catalog.Admin.Id, UserRole.Pharmacist);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Context)
                .AddScoped(_ => new GstReturnExportService(database.Context))
                .AddScoped(_ => database.CreateGstService())
                .AddScoped(_ => new ProductTaxService(database.Context))
                .AddSingleton(new ActiveStoreContext(database.Storage.RootDirectory))
                .BuildServiceProvider();
            var gst = new GstReturnsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), null!);
            await gst.LoadAsync();

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1366, Height = 900 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = gst, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(GstReturnsPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();

            Border box = Ui.Named<Border>(host, "GST calculation summary");
            string sum = Descendants(box).OfType<TextBlock>().Select(t => t.Text).Single(t => t.Contains("eligible ITC"));
            // ₹60 at 12% inclusive: ₹6.43 GST, split ₹3.22 CGST / ₹3.21 SGST, as the invoice prints it.
            Assert.StartsWith("₹3.22 CGST + ₹3.21 SGST + ₹0.00 IGST − ", sum);
            Assert.Equal(gst.CalculationSummary, sum);
            Assert.Contains(gst.NetGstPayableText, sum);
            host.Close();
        });
    }

    private static async Task<DashboardPageViewModel> LoadedDashboardAsync(ServiceProvider services, OnboardingSettingsStore settings)
    {
        // The dashboard's other panels need the licence and sync services; the checklist needs only the records.
        var dashboard = new DashboardPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), null!, null!, null!, settings);
        await (Task)typeof(DashboardPageViewModel).GetMethod("RefreshOnboardingAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dashboard, [CancellationToken.None])!;
        return dashboard;
    }

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

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
