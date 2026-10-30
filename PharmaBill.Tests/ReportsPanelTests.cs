using System.Data;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using QuestPDF.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PharmaBill.Tests;

// Reports & Daily MIS: the report types are buttons in a panel down the right, grouped. Every one, pressed on screen,
// runs at once for the period already chosen, shows rows from a shop with real trading, and exports to PDF and Excel.
[Collection(RunsAlone.Name)]
public sealed class ReportsPanelTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.ReportsPanel.{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class ExportTo(string folder) : IFilePickerService
    {
        public int Count { get; private set; }
        public string? Last { get; private set; }
        public string? PickExportDestination(string extension, string suggestedName) => Last = Path.Combine(folder, $"{++Count:00}-{suggestedName}.{extension}");
        public string? PickFolder(string title) => null;
        public string? PickBackupFile() => null;
        public string? PickLicenceDocument() => null;
        public string? PickCsvFile() => null;
        public string? PickPurchaseDocument() => null;
        public IReadOnlyList<string> PickInvoiceDocuments() => [];
        public string? PickPurchaseSpreadsheet() => null;
        public string? PickCustomerExcel() => null;
        public string? PickPrescriptionDocument() => null;
        public string? PickSyncPackage() => null;
    }

    private sealed class NoDialog : IInvoiceDetailDialogService
    {
        public void Show(InvoiceDetail detail) { }
    }

    private sealed class NoViewer : ISupplierBillViewer
    {
        public void Show(PurchaseBillAttachment bill, string path) { }
    }

    // A shop with a month's trading: a purchase (one batch expiring this month), a retail bill with a doctor, a wholesale
    // credit invoice, a sales return, a purchase return and a supplier payment, with one medicine below its reorder level.
    private static async Task SeedAsync(DatabaseTestContext database)
    {
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);
        var allowed = new Allowed();
        Supplier supplier = catalog.Suppliers[0];
        DateOnly today = PharmaBill.Core.BusinessClock.Today;
        DateOnly monthEnd = new(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        PurchaseInvoice bought = await database.CreatePurchaseService(allowed).SavePurchaseAsync(
            new SavePurchaseInput(supplier.Id, "RP-1", today, 1000m, 0m, 120m, 1120m,
            [
                new PurchaseLineInput(catalog.Cetirizine10.Id, "CET-EXP", monthEnd, 50m, 0m, 30m, 20m, 12m, 0m, 1000m, null)
            ]), catalog.Admin.Id, UserRole.Owner);

        Patient patient = catalog.RetailPatients[1];
        RetailSaleResult sale = await database.CreateRetailBillingService(allowed).SaveSaleAsync(
            new SaveRetailSaleInput(patient.Name, patient.Phone!, patient.Address, "Dr. Rehana Qadri", "JKMC-4411", null,
                [new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 4m, 0m, 30m), new RetailSaleLineInput(catalog.Amoxicillin500.Id, catalog.AmoxicillinBatch.Id, 2m, 0m, catalog.AmoxicillinBatch.Mrp!.Value)],
                [new RetailPaymentInput("Cash", 120m + 2m * catalog.AmoxicillinBatch.Mrp!.Value)], false),
            catalog.Admin.Id, UserRole.Pharmacist);
        await database.CreateWholesaleInvoiceService(allowed).SaveAsync(
            new SaveWholesaleInvoiceInput(catalog.BurhanMedicalHall.Id, [new WholesaleInvoiceLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 5m, 0m, 28m)],
                0m, "Credit", null, false, 90, false, null, null, null, null, null, "Reports panel test"),
            catalog.Admin.Id, UserRole.Owner);

        RetailBillingService billing = database.CreateRetailBillingService(allowed);
        RetailSaleReturnableLine returnable = (await billing.GetSaleReturnLinesAsync(sale.Sale.Id)).First();
        await billing.IssueSalesReturnAsync(sale.Sale.Id, [new RetailSaleReturnLineInput(returnable.SaleItemId, 1m)], "Wrong strength", true, catalog.Admin.Id, UserRole.Owner);

        Guid batchId = await database.Context.Batches.Where(batch => batch.BatchNo == "CET-EXP").Select(batch => batch.Id).SingleAsync();
        await database.CreatePurchaseService(allowed).SavePurchaseReturnAsync(supplier.Id, "PR-1", today, [new PurchaseReturnLineInput(batchId, 5m)], "Damaged strips", catalog.Admin.Id, UserRole.Owner);

        // An expiry claim sent and part credited: both expiry claim reports have a row.
        var claimsUow = new UnitOfWork(database.Context);
        var claims = new ExpiryReturnService(claimsUow, new NumberSeriesService(claimsUow), allowed);
        ExpiryClaim claim = await claims.CreateDraftAsync(supplier.Id, [new ExpiryClaimLineRequest(batchId, 10m)], null, catalog.Admin.Id, UserRole.Owner);
        await claims.MarkSentAsync(claim.Id, today, "By hand", null, catalog.Admin.Id, UserRole.Owner);
        Guid claimLine = await database.Context.ExpiryClaimLines.Where(line => line.ExpiryClaimId == claim.Id).Select(line => line.Id).SingleAsync();
        await claims.RecordCreditNoteAsync(claim.Id, new ExpiryCreditInput("CN-EXP-1", today, 100m, 89.29m, 5.36m, 5.35m, 0m, ExpiryCreditSettlement.AdjustedAgainstPurchases, [new ExpiryCreditLineInput(claimLine, 100m, null)]), catalog.Admin.Id, UserRole.Owner);

        database.Context.Receipts.Add(new Receipt { ReceiptNo = "PAY-1", SupplierId = supplier.Id, Amount = 500m, PaymentMethod = "Bank", ReceiptAtUtc = PharmaBill.Core.BusinessClock.UtcNow });
        Drug metformin = await database.Context.Drugs.SingleAsync(drug => drug.Id == catalog.Metformin500.Id);
        metformin.ReorderLevel = 100000m;
        await database.Context.SaveChangesAsync();
        _ = bought;
    }

    [Fact]
    public void EveryReport_IsAButtonThatRunsAtOnce_ShowsRows_AndExportsToPdfAndExcel()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Directory.CreateDirectory(_root);
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            await SeedAsync(database);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Context)
                .AddScoped(_ => new ReportsDashboardService(database.Context, database.CreateBranchService(), new SalesAnalysisService(database.Context)))
                .BuildServiceProvider();
            var picker = new ExportTo(_root);
            var page = new ReportsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), picker, new TabularExportService(), new NoDialog(), new NoViewer(), new DataRetentionSettingsStore(database.Storage));

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(ReportsPageViewModel))], Resources = mainWindow.Resources };
            await page.LoadAsync();
            await Ui.SettleAsync();

            // The dropdown is gone; every report it held is in the panel, once, under a heading.
            Assert.DoesNotContain(Ui.Descendants(host).OfType<ComboBox>(), box => box.ItemsSource == page.ReportChoices);
            ReportPanelItem[] items = page.ReportGroups.SelectMany(group => group.Items).ToArray();
            Assert.Equal(page.ReportChoices.Select(choice => choice.Name).OrderBy(name => name), items.Select(item => item.Name).OrderBy(name => name));
            Assert.Equal(["Sales", "Purchases & suppliers", "Stock & product", "Profit & accounts", "Compliance & people"], page.ReportGroups.Select(group => group.Heading).ToArray());
            Assert.True(Assert.Single(items, item => item.IsSelected).Choice == page.SelectedReport);

            List<string> empty = new List<string>();
            foreach (ReportPanelItem item in items)
            {
                DataView? before = page.Table;
                Button button = Ui.Single<Button>(host, candidate => candidate.IsVisible && AutomationProperties.GetName(candidate) == item.Name && candidate.DataContext == item);
                await Ui.PressAsync(button);
                if (item.Choice != items[0].Choice || before == null)
                {
                    // Runs on its own: no Refresh press.
                    await Ui.UntilAsync(() => page.Table != before || page.ErrorMessage.Length > 0, item.Name + " to run");
                }
                await Ui.SettleAsync();
                Assert.True(page.ErrorMessage.Length == 0, item.Name + ": " + page.ErrorMessage);
                Assert.Same(item.Choice, page.SelectedReport);
                Assert.True(item.IsSelected, item.Name + " is not marked as selected");
                Assert.Single(items, other => other.IsSelected);

                DataTable table = page.Table!.Table!;
                string[] columns = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).Where(name => name != "__key").ToArray();
                // A row with something other than totals in its first column: data, not only the summary lines.
                int dataRows = page.Table.Cast<DataRowView>().Count(row => columns.Skip(0).Take(3).Any(column => Convert.ToString(row[column]) is { Length: > 0 } text && !text.StartsWith("Total", StringComparison.OrdinalIgnoreCase)));
                output.WriteLine($"{item.Name}: {page.Table.Count} row(s), {dataRows} with data; columns {string.Join(" | ", columns)}");
                if (dataRows == 0)
                {
                    empty.Add(item.Name);
                }

                await Ui.PressAsync(Ui.Single<Button>(host, b => b.IsVisible && b.Command == page.ExportPdfCommand));
                string pdf = picker.Last!;
                await Ui.UntilAsync(() => File.Exists(pdf), item.Name + " PDF");
                using (FileStream file = File.OpenRead(pdf))
                {
                    byte[] head = new byte[4];
                    file.ReadExactly(head);
                    Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(head));
                }

                await Ui.PressAsync(Ui.Single<Button>(host, b => b.IsVisible && b.Command == page.ExportExcelCommand));
                string xlsx = picker.Last!;
                await Ui.UntilAsync(() => File.Exists(xlsx), item.Name + " Excel");
                using var workbook = new XLWorkbook(xlsx);
                IXLRow header = workbook.Worksheets.First().RowsUsed().First(row => row.Cell(1).GetString() == columns[0]);
                Assert.Equal(columns, header.Cells(1, columns.Length).Select(cell => cell.GetString()).ToArray());
                Assert.True(page.ErrorMessage.Length == 0, item.Name + " export: " + page.ErrorMessage);
            }

            Assert.True(empty.Count == 0, "Reports that ran but showed no data: " + string.Join(", ", empty));

            // No way to collapse the panel: it is always open at its full width.
            Assert.DoesNotContain(Ui.Descendants(host).OfType<Button>(), b => AutomationProperties.GetName(b) == "Collapse or expand report types");
            Assert.Equal(264, Ui.Named<Border>(host, "Report types").ActualWidth, 0);

            // Custom opens its date range beside the chips; Apply runs it and keeps Custom chosen.
            StackPanel custom = Ui.Named<StackPanel>(host, "Custom period", visibleOnly: false);
            Assert.False(custom.IsVisible);
            await Ui.PressAsync(Ui.Single<Button>(host, b => b.IsVisible && b.DataContext is PeriodChip { Key: "Custom" }));
            await Ui.SettleAsync();
            Assert.True(custom.IsVisible);
            page.FromDate = new DateTime(2026, 10, 5);
            await Ui.PressAsync(Ui.Single<Button>(host, b => b.IsVisible && AutomationProperties.GetName(b) == "Run custom period"));
            await Ui.UntilAsync(() => page.SummaryRange.StartsWith("5 Oct 2026"), "the custom period to run");
            Assert.True(page.PeriodChips.Single(chip => chip.Key == "Custom").IsSelected);
            Assert.True(custom.IsVisible);

            // The summary strip: report, dates, branch and rows on one line.
            await Ui.PressAsync(Ui.Single<Button>(host, b => b.IsVisible && b.DataContext is ReportPanelItem { Name: "Sales Summary (General)" }));
            await Ui.UntilAsync(() => page.SummaryReport == "Sales Summary (General)", "the summary");
            Assert.Equal("Current branch only", page.SummaryBranch);
            Assert.EndsWith("rows", page.SummaryRows);
            Assert.True(Ui.Named<Border>(host, "Report summary").IsVisible);

            // Date, Invoice and Item Name stay in view when the grid scrolls sideways.
            await Ui.SettleAsync();
            DataGrid grid = Ui.Single<DataGrid>(host, g => g.ItemsSource == page.Table);
            Assert.Equal(4, grid.FrozenColumnCount);
            Assert.Equal("Item Name", grid.Columns.OrderBy(column => column.DisplayIndex).ElementAt(3).Header);
            host.Close();
        });
    }

    [Fact]
    public void OnASmallWindow_ThePanelStaysOpen_AndThePeriodArrowsStayTogether()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Context)
                .AddScoped(_ => new ReportsDashboardService(database.Context, database.CreateBranchService(), new SalesAnalysisService(database.Context)))
                .BuildServiceProvider();
            var page = new ReportsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), null!, new TabularExportService(), new NoDialog(), new NoViewer(), new DataRetentionSettingsStore(database.Storage));
            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1000, Height = 800 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(ReportsPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            // Full width and every report name showing; the results grid takes what is left, and scrolls.
            Assert.Equal(264, Ui.Named<Border>(host, "Report types").ActualWidth, 0);
            Assert.All(page.ReportGroups.SelectMany(group => group.Items), item =>
                Assert.Contains(Ui.Descendants(host).OfType<TextBlock>(), text => text.IsVisible && text.Text == item.Name));

            // Previous and Next sit side by side, on the same line as the month and year.
            Button previous = Ui.Single<Button>(host, b => b.IsVisible && AutomationProperties.GetName(b) == "Previous month");
            Button next = Ui.Single<Button>(host, b => b.IsVisible && AutomationProperties.GetName(b) == "Next month");
            ComboBox year = Ui.Single<ComboBox>(host, box => box.IsVisible && box.ItemsSource == page.Years);
            Point p = previous.TranslatePoint(new Point(0, 0), host), n = next.TranslatePoint(new Point(0, 0), host), y = year.TranslatePoint(new Point(0, 0), host);
            Assert.Equal(p.Y, n.Y, 1);
            Assert.InRange(Math.Abs(y.Y - p.Y), 0, 4); // the same line (the box and the buttons differ by a pixel)
            Assert.InRange(n.X - (p.X + previous.ActualWidth), 0, 12);
            host.Close();
        });
    }
}
