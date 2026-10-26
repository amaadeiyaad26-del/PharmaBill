using System.Data;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Sale Book (daily / monthly) in Reports & Daily MIS: every invoice line of the period, as billed, each row opening its invoice.
public sealed class SaleBookReportTests
{
    private static readonly string[] Columns = ["Date & Time", "Invoice No", "Patient / Customer", "Item Name", "B.No", "Expiry (MM/YY)", "Qty", "Rate", "Total Amount", "Branch"];

    [Fact]
    public void SaleBook_ListsEveryLineAsBilled_AndARowOpensItsInvoice()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            Shop shop = await SeedAsync(database);
            // Renaming the medicine afterwards must not rewrite the book: it shows what was billed.
            await database.Context.Database.ExecuteSqlRawAsync("UPDATE Drugs SET Name = 'Renamed later' WHERE Id = {0}", shop.Catalog.Paracetamol650.Id);

            var dialog = new RecordingInvoiceDialog();
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Context)
                .AddScoped(_ => Reports(database))
                .BuildServiceProvider();
            var page = new ReportsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), null!, new TabularExportService(), dialog, new NoBillViewer(), new DataRetentionSettingsStore(database.Storage));

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 900 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(ReportsPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();

            foreach (string report in new[] { "Sale Book (daily)", "Sale Book (monthly)" })
            {
                ComboBox reports = Ui.Single<ComboBox>(host, box => box.ItemsSource == page.ReportChoices);
                reports.SelectedItem = page.ReportChoices.Single(choice => choice.Name == report);
                await Ui.UntilAsync(() => page.PeriodLabel.EndsWith(report), report + " to load");

                DataGrid grid = Ui.Single<DataGrid>(host, g => g.ItemsSource == page.Table);
                Assert.Equal(Columns, grid.Columns.Select(column => column.Header.ToString()).ToArray());
                // Two lines on the retail bill, one on the wholesale invoice: one row per line.
                Assert.Equal(3, page.Table!.Count);
                DataRowView[] rows = page.Table.Cast<DataRowView>().ToArray();
                Assert.Equal(["Paracetamol 650mg", "Amoxicillin 500mg", "Paracetamol 650mg"], rows.Select(r => (string)r["Item Name"]).ToArray());
                Assert.Equal(shop.Retail.InvoiceNo, rows[0]["Invoice No"]);
                Assert.Equal(shop.Retail.InvoiceNo, rows[1]["Invoice No"]);
                Assert.Equal(shop.Wholesale.InvoiceNo, rows[2]["Invoice No"]);
                Assert.Equal(shop.Catalog.RetailPatients[1].Name, rows[0]["Patient / Customer"]);
                Assert.Equal(shop.Catalog.ParacetamolBatch.BatchNo, rows[0]["B.No"]);
                Assert.Equal(ReportsDashboardService.FormatExpiry(shop.Catalog.ParacetamolBatch.ExpiryDate), rows[0]["Expiry (MM/YY)"]);
                Assert.Equal("2", rows[0]["Qty"]);
                Assert.Equal(DateTime.Parse(rows[0]["Date & Time"].ToString()!).Date, BusinessClock.Now.Date);

                // The invoice number is a link to the Invoice details popup.
                dialog.Shown.Clear();
                Button link = Ui.Single<Button>(host, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Open invoice" && Equals(b.Content, shop.Wholesale.InvoiceNo));
                await Ui.PressAsync(link);
                await Ui.UntilAsync(() => dialog.Shown.Count == 1, "the invoice to open");
                Assert.Equal(shop.Wholesale.InvoiceNo, dialog.Shown[0].InvoiceNo);
                Assert.True(dialog.Shown[0].IsWholesale);
            }
            host.Close();
        });
    }

    [Fact]
    public async Task EachLine_ShowsItsOwnBranch_InTheNetworkView()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SeedAsync(database);
        var north = new Branch { Code = "BR02-NORTH", BranchName = "North", InvoicePrefix = "BR02-INV" };
        database.Context.Branches.Add(north);
        await database.Context.SaveChangesAsync();
        await database.Context.Database.ExecuteSqlRawAsync("UPDATE WholesaleInvoices SET BranchId = {0} WHERE Id = {1}", north.Id, shop.Wholesale.Id);

        ReportTable book = await Reports(database).GetReportAsync(ReportKind.SaleBookDaily, BusinessClock.Today, BusinessClock.Today, ReportBranchScope.ConsolidatedNetwork);

        string current = database.CreateBranchService().CurrentSettings.CurrentBranchCode;
        Assert.Equal([current, current, "BR02-NORTH"], book.Rows.Select(row => row[9]).ToArray());
        Assert.Equal([$"R|{shop.Retail.Id}", $"R|{shop.Retail.Id}", $"W|{shop.Wholesale.Id}"], book.RowKeys);
    }

    private static ReportsDashboardService Reports(DatabaseTestContext database) =>
        new(database.Context, database.CreateBranchService(), new SalesAnalysisService(database.Context));

    private sealed record Shop(E2EHealthSeed.Catalog Catalog, Sale Retail, WholesaleInvoice Wholesale);

    private static async Task<Shop> SeedAsync(DatabaseTestContext database)
    {
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);
        var allowed = new Allowed();
        var patient = catalog.RetailPatients[1];
        RetailSaleResult sale = await database.CreateRetailBillingService(allowed).SaveSaleAsync(
            new SaveRetailSaleInput(patient.Name, patient.Phone!, patient.Address, null, null, null,
                [new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 2m, 0m, 30m), new RetailSaleLineInput(catalog.Amoxicillin500.Id, catalog.AmoxicillinBatch.Id, 1m, 0m, catalog.AmoxicillinBatch.Mrp!.Value)],
                [new RetailPaymentInput("Cash", 60m + catalog.AmoxicillinBatch.Mrp!.Value)], false),
            catalog.Admin.Id, UserRole.Pharmacist);
        WholesaleInvoiceResult invoice = await database.CreateWholesaleInvoiceService(allowed).SaveAsync(
            new SaveWholesaleInvoiceInput(catalog.BurhanMedicalHall.Id, [new WholesaleInvoiceLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 5m, 0m, 28m)],
                0m, "Credit", null, false, 90, false, null, null, null, null, null, "Sale book test"),
            catalog.Admin.Id, UserRole.Owner);
        // The suite's clock is pinned, so lines saved together share a timestamp; the real clock moves on between them.
        await database.Context.Database.ExecuteSqlRawAsync("UPDATE SaleItems SET CreatedAtUtc = {0} WHERE SaleId = {1} AND DrugId = {2}", sale.Sale.SaleAtUtc.AddSeconds(1), sale.Sale.Id, catalog.Amoxicillin500.Id);
        // The wholesale invoice is billed a minute after the retail bill, so the book's order is fixed.
        await database.Context.Database.ExecuteSqlRawAsync("UPDATE WholesaleInvoices SET InvoiceAtUtc = {0} WHERE Id = {1}", sale.Sale.SaleAtUtc.AddMinutes(1), invoice.Invoice.Id);
        return new Shop(catalog, sale.Sale, invoice.Invoice);
    }

    private sealed class NoBillViewer : ISupplierBillViewer
    {
        public void Show(PurchaseBillAttachment bill, string path) => throw new InvalidOperationException("No supplier bill is opened from the Sale Book.");
    }

    private sealed class RecordingInvoiceDialog : IInvoiceDetailDialogService
    {
        public List<InvoiceDetail> Shown { get; } = [];

        public void Show(InvoiceDetail detail) => Shown.Add(detail);
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
