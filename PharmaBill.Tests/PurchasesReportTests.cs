using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace PharmaBill.Tests;

// The Purchases (invoices) report: each supplier bill with its payment status, amounts and branch, and the supplier's own
// attached bill opened from its row; the export carries the same columns.
[Collection(RunsAlone.Name)]
public sealed class PurchasesReportTests : IDisposable
{
    private const string Supplier = "Kashmir Pharma Distributors";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.PurchasesReport.{Guid.NewGuid():N}");

    public PurchasesReportTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task PaymentStatus_SettlesTheOldestBillsFirst()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Bills bills = await SeedAsync(database);

        ReportTable report = await Reports(database).GetReportAsync(ReportKind.Purchases, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 21));

        Assert.Equal(ReportsDashboardService.PurchaseColumns, report.Columns);
        var ours = report.Rows.Where(row => row[3] == Supplier).ToDictionary(row => row[2]);
        // ₹150 paid and ₹20 returned settle A (₹100) and ₹70 of B (₹200); C is untouched; D never reached the ledger.
        Assert.Equal(("Paid", "0.00"), (ours["A-100"][4], ours["A-100"][5]));
        Assert.Equal(("Partially paid", "130.00"), (ours["B-200"][4], ours["B-200"][5]));
        Assert.Equal(("Unpaid", "50.00"), (ours["C-50"][4], ours["C-50"][5]));
        Assert.Equal(("Not in ledger", ""), (ours["D-30"][4], ours["D-30"][5]));
        Assert.Equal(["Attached", "Attached", "—", "—"], new[] { "A-100", "B-200", "C-50", "D-30" }.Select(no => ours[no][10]).ToArray());
        Assert.Equal("01-Oct-2026", ours["A-100"][0]);
        Assert.Equal(["89.29", "10.71", "100.00"], new[] { ours["A-100"][6], ours["A-100"][7], ours["A-100"][8] });
        Assert.Equal(report.Rows.Count, report.RowKeys!.Count);
        Assert.Equal("P|" + bills.A.Id, report.RowKeys[report.Rows.ToList().FindIndex(row => row[2] == "A-100")]);
    }

    [Fact]
    public void PurchasesReport_ViewBill_OpensTheAttachedBill_AndExportCarriesTheColumns()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            Bills bills = await SeedAsync(database);
            var viewer = new RecordingViewer();
            var picker = new ExportPicker { Destination = Path.Combine(_root, "purchases.xlsx") };
            ServiceProvider services = new ServiceCollection().AddSingleton(database.Context).AddScoped(_ => Reports(database)).BuildServiceProvider();
            var page = new ReportsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), picker, new TabularExportService(), new NoInvoiceDialog(), viewer, new DataRetentionSettingsStore(database.Storage));

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1600, Height = 900 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(ReportsPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();

            await Ui.PressAsync(Ui.Single<Button>(host, b => b.IsVisible && b.DataContext is ReportPanelItem && System.Windows.Automation.AutomationProperties.GetName(b) == "Purchases (invoices)"));
            await Ui.UntilAsync(() => page.PeriodLabel.EndsWith("Purchases"), "the purchases report to load");
            DataGrid grid = Ui.Single<DataGrid>(host, g => g.ItemsSource == page.Table);
            Assert.Equal(ReportsDashboardService.PurchaseColumns, grid.Columns.Select(column => column.Header.ToString()).ToArray());

            // A has its PDF: the link opens it.
            DataRowView rowA = page.Table!.Cast<DataRowView>().Single(row => (string)row["Bill No"] == "A-100");
            grid.ScrollIntoView(rowA);
            await Ui.SettleAsync();
            await Ui.PressAsync(Ui.Single<Button>(host, b => System.Windows.Automation.AutomationProperties.GetName(b) == "View supplier bill" && b.DataContext == rowA));
            await Ui.UntilAsync(() => viewer.Shown.Count == 1, "the bill to open");
            Assert.Equal((bills.A.Id, bills.PdfPath), (viewer.Shown[0].Bill.PurchaseId, viewer.Shown[0].Path));

            // B's file was moved or deleted: the page says so instead of opening nothing.
            DataRowView rowB = page.Table.Cast<DataRowView>().Single(row => (string)row["Bill No"] == "B-200");
            grid.ScrollIntoView(rowB);
            await Ui.SettleAsync();
            await Ui.PressAsync(Ui.Single<Button>(host, b => System.Windows.Automation.AutomationProperties.GetName(b) == "View supplier bill" && b.DataContext == rowB));
            Assert.Contains("no longer at", page.ErrorMessage);
            Assert.Single(viewer.Shown);

            // C has nothing attached: no link on its row.
            DataRowView rowC = page.Table.Cast<DataRowView>().Single(row => (string)row["Bill No"] == "C-50");
            Assert.DoesNotContain(Descendants(host).OfType<Button>(), b => b.DataContext == rowC && b.IsVisible && System.Windows.Automation.AutomationProperties.GetName(b) == "View supplier bill");

            // Export to Excel: the same columns and values as on screen.
            await Ui.PressAsync(Ui.Single<Button>(host, b => b.Command == page.ExportExcelCommand));
            await Ui.UntilAsync(() => File.Exists(picker.Destination!), "the export");
            using var workbook = new XLWorkbook(picker.Destination);
            IXLWorksheet sheet = workbook.Worksheets.First();
            IXLRow header = sheet.RowsUsed().First(row => row.Cell(1).GetString() == "Bill Date");
            Assert.Equal(ReportsDashboardService.PurchaseColumns, header.Cells(1, ReportsDashboardService.PurchaseColumns.Count).Select(cell => cell.GetString()).ToArray());
            IXLRow exportedA = sheet.RowsUsed().Single(row => row.Cell(3).GetString() == "A-100");
            Assert.Equal(("Paid", "Attached"), (exportedA.Cell(5).GetString(), exportedA.Cell(11).GetString()));
            host.Close();
        });
    }

    [Fact]
    public void Viewer_ShowsPdfAndImageBills_ZoomsAndHandsOffToWindows()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        string pdf = Path.Combine(_root, "bill.pdf");
        Document.Create(document =>
        {
            document.Page(page => page.Content().Text("Supplier bill page 1"));
            document.Page(page => page.Content().Text("Supplier bill page 2"));
        }).GeneratePdf(pdf);
        string png = Path.Combine(_root, "bill.png");
        var bitmap = new RenderTargetBitmap(400, 600, 96, 96, PixelFormats.Pbgra32);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream file = File.Create(png))
        {
            encoder.Save(file);
        }

        Ui.Run(async () =>
        {
            foreach ((string path, int pages) in new[] { (pdf, 2), (png, 1) })
            {
                var window = new SupplierBillViewerWindow(new PurchaseBillAttachment(Guid.NewGuid(), "A-100", Supplier, path), path);
                string? handedOff = null;
                window.OpenExternally = file => handedOff = file;
                Ui.Prepare(window);
                window.Show();
                await Ui.UntilAsync(() => window.LoadedPages.Count == pages, Path.GetFileName(path) + " to load");
                Assert.Equal(pages == 1 ? "1 page" : "2 pages", ((TextBlock)window.FindName("StatusText")).Text);

                double fitted = window.PageWidth;
                await Ui.PressAsync(Ui.Single<Button>(window, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Zoom in"));
                Assert.True(window.PageWidth > fitted);
                await Ui.PressAsync(Ui.Button(window, "Fit width"));
                Assert.Equal(fitted, window.PageWidth, 1);
                await Ui.PressAsync(Ui.Button(window, "Open in default viewer"));
                Assert.Equal(path, handedOff);
                window.Close();
            }
        });
    }

    private static ReportsDashboardService Reports(DatabaseTestContext database) =>
        new(database.Context, database.CreateBranchService(), new SalesAnalysisService(database.Context));

    private sealed record Bills(PurchaseInvoice A, PurchaseInvoice B, string PdfPath);

    // One supplier, four bills in October, ₹150 paid and ₹20 of goods returned (both on account).
    private async Task<Bills> SeedAsync(DatabaseTestContext database)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        await E2EHealthSeed.SeedAsync(database.Context);
        var supplier = new Supplier { Name = Supplier };
        database.Context.Suppliers.Add(supplier);
        string pdf = Path.Combine(_root, "A-100.pdf");
        Document.Create(document => document.Page(page => page.Content().Text("A-100"))).GeneratePdf(pdf);

        PurchaseInvoice Bill(string no, int day, decimal total, string? attachment, bool inLedger)
        {
            var invoice = new PurchaseInvoice
            {
                SupplierId = supplier.Id, InvoiceNo = no, InvoiceDate = new DateOnly(2026, 10, day), Status = PurchaseInvoice.PostedStatus,
                Subtotal = Math.Round(total / 1.12m, 2), TaxAmount = total - Math.Round(total / 1.12m, 2), TotalAmount = total, AttachedInvoicePath = attachment
            };
            database.Context.PurchaseInvoices.Add(invoice);
            if (inLedger)
            {
                database.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
                {
                    SupplierId = supplier.Id, EntryAtUtc = invoice.InvoiceDate.StartOfDayUtc(), EntryType = LedgerEntryTypes.PurchaseBill,
                    ReferenceId = invoice.Id, ReferenceNo = no, Credit = total
                });
            }
            return invoice;
        }

        PurchaseInvoice a = Bill("A-100", 1, 100m, pdf, inLedger: true);
        PurchaseInvoice b = Bill("B-200", 5, 200m, Path.Combine(_root, "moved-away.png"), inLedger: true);
        Bill("C-50", 10, 50m, null, inLedger: true);
        Bill("D-30", 12, 30m, null, inLedger: false);
        database.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry { SupplierId = supplier.Id, EntryAtUtc = new DateOnly(2026, 10, 15).StartOfDayUtc(), EntryType = LedgerEntryTypes.SupplierPayment, Debit = 150m });
        database.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry { SupplierId = supplier.Id, EntryAtUtc = new DateOnly(2026, 10, 16).StartOfDayUtc(), EntryType = LedgerEntryTypes.PurchaseReturn, Debit = 20m });
        await database.Context.SaveChangesAsync();
        return new Bills(a, b, pdf);
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

    private sealed class RecordingViewer : ISupplierBillViewer
    {
        public List<(PurchaseBillAttachment Bill, string Path)> Shown { get; } = [];

        public void Show(PurchaseBillAttachment bill, string path) => Shown.Add((bill, path));
    }

    private sealed class NoInvoiceDialog : IInvoiceDetailDialogService
    {
        public void Show(InvoiceDetail detail) => throw new InvalidOperationException("Purchases rows open the supplier bill, not an invoice.");
    }

    private sealed class ExportPicker : IFilePickerService
    {
        public string? Destination { get; set; }
        public string? PickExportDestination(string extension, string suggestedName) => Destination;
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
}
