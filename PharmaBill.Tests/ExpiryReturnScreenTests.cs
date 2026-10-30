using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using QuestPDF.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PharmaBill.Tests;

// Expiry returns used through the screens: a supplier given a policy on the Suppliers page, stock bought on a purchase
// with a near-expiry batch, a claim built on Expiry returns, printed, sent, part credited, a line rejected and written
// off, the supplier's ledger on Accounts, and both reports on the Reports page.
[Collection(RunsAlone.Name)]
public sealed class ExpiryReturnScreenTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.ExpiryScreens.{Guid.NewGuid():N}");

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

    private sealed class SaveTo(string folder) : IFilePickerService
    {
        public string? Last { get; private set; }
        public string? PickExportDestination(string extension, string suggestedName) => Last = Path.Combine(folder, suggestedName + "." + extension);
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

    private sealed class Asks : IConfirmationService
    {
        public List<string> Questions { get; } = [];
        public bool Confirm(string message, string title) { Questions.Add(message); return true; }
        public bool? Choose(string message, string title, string acceptLabel, string alternateLabel) { Questions.Add(message); return true; }
        public void Notify(string title, string message) { }
        public void NotifyError(string title, string message) => throw new InvalidOperationException(message);
    }

    private sealed class NoDialog : IInvoiceDetailDialogService
    {
        public void Show(InvoiceDetail detail) { }
    }

    private sealed class NoViewer : ISupplierBillViewer
    {
        public void Show(PurchaseBillAttachment bill, string path) { }
    }

    private static void TypeAndLeave(TextBox box, string text)
    {
        Ui.Type(box, text);
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private static Window Host(object page, PharmaBill.App.MainWindow mainWindow)
    {
        var host = new Window { Width = 1500, Height = 1000 };
        Ui.Prepare(host);
        host.Show();
        host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(page.GetType())], Resources = mainWindow.Resources };
        return host;
    }

    private void Snapshot(Window host, string name)
    {
        host.UpdateLayout();
        var element = (FrameworkElement)host.Content;
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var white = new DrawingVisual();
        using (DrawingContext dc = white.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }
        bitmap.Render(white);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string folder = Environment.GetEnvironmentVariable("PHARMAREMIT_SCREENSHOTS") is { Length: > 0 } configured ? configured : _root;
        Directory.CreateDirectory(folder);
        using FileStream stream = File.Create(Path.Combine(folder, name + ".png"));
        encoder.Save(stream);
        output.WriteLine("screenshot: " + Path.Combine(folder, name + ".png"));
    }

    [Fact]
    public void ExpiryReturn_ThroughTheScreens_FromSupplierPolicyToBothReports()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Directory.CreateDirectory(_root);
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            E2EHealthSeed.Catalog catalog = await E2EHealthSeed.SeedAsync(database.Context);
            var allowed = new Allowed();
            var session = new CurrentSession();
            session.SignIn(catalog.Admin);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(session)
                .AddScoped(_ => database.OpenContext())
                .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
                .AddSingleton<IEntitlementService>(allowed)
                .AddScoped<NumberSeriesService>()
                .AddScoped<ExpiryReturnService>()
                .AddScoped(sp => new WholesaleAccountsService(sp.GetRequiredService<IUnitOfWork>()))
                .AddScoped(sp => new ReportsDashboardService(sp.GetRequiredService<PharmaBillDbContext>(), database.CreateBranchService(), new SalesAnalysisService(sp.GetRequiredService<PharmaBillDbContext>())))
                .BuildServiceProvider();
            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            IServiceScopeFactory scopes = services.GetRequiredService<IServiceScopeFactory>();

            // 1. Suppliers: a new distributor with its own policy, 120 days before to 30 after, and its stated policy.
            var suppliersPage = new SupplierDirectoryPageViewModel(scopes);
            await suppliersPage.LoadAsync();
            Window suppliersHost = Host(suppliersPage, mainWindow);
            await Ui.SettleAsync();
            TypeAndLeave(Ui.Single<TextBox>(suppliersHost, box => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Name"), "Kashmir Medical Agencies");
            TypeAndLeave(Ui.Single<TextBox>(suppliersHost, box => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Gstin"), "01AAAAA1111A1ZA");
            TypeAndLeave(Ui.Single<TextBox>(suppliersHost, box => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path.Path == "Address"), "Residency Road, Srinagar");
            TypeAndLeave(Ui.Named<TextBox>(suppliersHost, "Supplier days before expiry"), "120");
            TypeAndLeave(Ui.Named<TextBox>(suppliersHost, "Supplier days after expiry"), "30");
            TypeAndLeave(Ui.Named<TextBox>(suppliersHost, "Supplier expiry policy note"), "Expiry accepted 4 months before to 1 month after; credit at net purchase rate against the next bill.");
            await Ui.PressAsync(Ui.Button(suppliersHost, "Save supplier"));
            // Saving reselects the supplier, which clears the page's message: the saved record is checked below.
            Assert.True(string.IsNullOrEmpty(suppliersPage.ErrorMessage), suppliersPage.ErrorMessage);
            Assert.Equal("120", suppliersPage.ExpiryDaysBefore);
            Snapshot(suppliersHost, "1-supplier-policy");
            suppliersHost.Close();
            database.Context.ChangeTracker.Clear();
            Supplier supplier = await database.Context.Suppliers.SingleAsync(item => item.Name == "Kashmir Medical Agencies");
            Assert.Equal((120, 30), (supplier.ExpiryReturnDaysBefore, supplier.ExpiryReturnDaysAfter));

            // 2. A purchase from it: a syrup batch expiring in 25 days, a tablet batch in 100 days, one far off.
            var syrup = new Drug { Name = "Benadryl Cough Syrup 100ml", Unit = "100 ML", HsnCode = "30049099", GstRate = 12m };
            var tablet = new Drug { Name = "Pantoprazole 40mg", Unit = "10 TAB", HsnCode = "30049011", GstRate = 12m };
            database.Context.AddRange(syrup, tablet);
            await database.Context.SaveChangesAsync();
            DateOnly today = BusinessClock.Today;
            await database.CreatePurchaseService(allowed).SavePurchaseAsync(new SavePurchaseInput(supplier.Id, "KMA/2041", today.AddDays(-180), 2050m, 100m, 246m, 2296m,
                [
                    new PurchaseLineInput(syrup.Id, "BEN2511", today.AddDays(25), 24m, 0m, 120m, 62.5m, 12m, 0m, 1500m, "S1"),
                    new PurchaseLineInput(tablet.Id, "PAN0226", today.AddDays(100), 30m, 0m, 25m, 20m, 12m, 100m, 500m, "T3"),
                    new PurchaseLineInput(tablet.Id, "PAN0927", today.AddDays(330), 5m, 0m, 25m, 10m, 12m, 0m, 50m, "T3")
                ]), catalog.Admin.Id, UserRole.Owner);
            decimal payableBefore = (await database.Context.SupplierLedgerEntries.Where(entry => entry.SupplierId == supplier.Id).ToListAsync()).Sum(entry => entry.Credit - entry.Debit);

            // 3. Expiry returns: claimable now, grouped under the supplier, with its policy.
            var picker = new SaveTo(_root);
            var asks = new Asks();
            var page = new ExpiryReturnsPageViewModel(scopes, session, picker, new TabularExportService(), asks);
            await page.LoadAsync();
            Window host = Host(page, mainWindow);
            await Ui.SettleAsync();
            ClaimableGroup group = Assert.Single(page.ClaimableGroups, item => item.SupplierId == supplier.Id);
            Assert.Contains("120 days before expiry to 30 days after", group.PolicyText);
            Assert.Contains("credit at net purchase rate", group.PolicyText);
            Assert.Equal(["BEN2511", "PAN0226"], group.Items.Select(item => item.Row.BatchNo).ToArray());
            Assert.Contains(Ui.Descendants(host).OfType<TextBlock>(), text => text.Text == ExpiryReturnService.ItcNote);
            // Tick both batches, claim 20 of the 24 syrups (part quantity) and all 30 tablets.
            DataGrid batches = Ui.Single<DataGrid>(host, grid => AutomationProperties.GetName(grid) == "Claimable batches" && grid.ItemsSource == group.Items);
            ClaimableItem syrupRow = group.Items.Single(item => item.Row.BatchNo == "BEN2511");
            ClaimableItem tabletRow = group.Items.Single(item => item.Row.BatchNo == "PAN0226");
            Assert.Equal((62.5m, 16.67m), (syrupRow.Row.Rate, tabletRow.Row.Rate));
            syrupRow.IsSelected = true;
            syrupRow.ClaimQuantity = "20";
            tabletRow.IsSelected = true;
            await Ui.SettleAsync();
            Assert.Contains("2 ticked", group.SelectedText);
            Snapshot(host, "2-claimable-now");
            await Ui.PressAsync(Ui.Single<Button>(host, button => button.IsVisible && AutomationProperties.GetName(button) == "Build draft claim" && button.CommandParameter == group));
            Assert.True(string.IsNullOrEmpty(page.ErrorMessage), page.ErrorMessage);
            Assert.Equal(1, page.SelectedTab);
            await (page.ClaimLoading ?? Task.CompletedTask);
            await Ui.SettleAsync();
            Assert.Equal("HC-EXP/2026-27/000001", page.Claim!.ClaimNo);
            Assert.Contains("50 unit(s), ₹1,750.10", page.HeldSummary);
            // 20 x 62.50 = 1250 + 150 GST = 1400; 30 x 16.67 = 500.10 + 60.01 = 560.11.
            Assert.Equal(1960.11m, page.Claim.TotalValue);
            Assert.Contains("Held for return", page.Lines.Select(line => line.State));
            Snapshot(host, "3-draft-claim");

            // Held stock cannot be sold; the 4 syrups not claimed still can.
            RetailBillingService billing = database.CreateRetailBillingService(allowed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => billing.SaveSaleAsync(new SaveRetailSaleInput("Walk-in", "9000000099", null, null, null, null,
                [new RetailSaleLineInput(syrup.Id, syrupRow.Row.BatchId, 5m, 0m, 120m)], [new RetailPaymentInput("Cash", 600m)], false), catalog.Admin.Id, UserRole.Owner));

            // 4. Print: the claim statement as PDF, and Excel.
            await Ui.PressAsync(Ui.Named<Button>(host, "Save claim PDF"));
            string pdf = picker.Last!;
            Assert.True(new FileInfo(pdf).Length > 5_000, "the claim statement PDF was not written");
            await Ui.PressAsync(Ui.Named<Button>(host, "Export claim to Excel"));
            string xlsx = picker.Last!;
            using (var workbook = new ClosedXML.Excel.XLWorkbook(xlsx))
            {
                string sheet = string.Join("\n", workbook.Worksheets.First().RowsUsed().Select(row => string.Join(" | ", row.CellsUsed().Select(cell => cell.GetString()))));
                output.WriteLine(sheet);
                Assert.Contains("Benadryl Cough Syrup 100ml | 100 ML | 30049099 | BEN2511", sheet);
                Assert.Contains("Total | 1750.10 | 105.01 | 105.00 | 0.00 | 1960.11", sheet);
            }
            File.Copy(pdf, Path.Combine(Environment.GetEnvironmentVariable("PHARMAREMIT_SCREENSHOTS") is { Length: > 0 } shots ? shots : _root, "4-claim-statement.pdf"), overwrite: true);

            // 5. Mark it sent.
            TypeAndLeave(Ui.Named<TextBox>(host, "Sent to"), "Mr. Bashir, returns desk");
            await Ui.PressAsync(Ui.Named<Button>(host, "Mark sent"));
            Assert.True(string.IsNullOrEmpty(page.ErrorMessage), page.ErrorMessage);
            await (page.ClaimLoading ?? Task.CompletedTask);
            await Ui.SettleAsync();
            Assert.Equal(ExpiryClaimStatus.Sent, page.Claim!.Status);
            Assert.Equal("Nothing is held for return.", page.HeldSummary);
            ExpiryClaimListRow open = Assert.Single(page.OpenClaims);
            Assert.Equal(("Sent", "0 days", "1,960.11"), (open.Status, open.Age, open.Balance));

            // 6. A part credit: credit note KMA/CN/88 for 1,250 against the syrup only, adjusted against purchases;
            //    the distributor's GST printed on it is 1,116.07 + 66.96 + 66.97. The rest is still owed.
            TypeAndLeave(Ui.Named<TextBox>(host, "Credit note number"), "KMA/CN/88");
            TypeAndLeave(Ui.Named<TextBox>(host, "Credit note total"), "1250");
            TypeAndLeave(Ui.Named<TextBox>(host, "Credit note taxable value"), "1116.07");
            TypeAndLeave(Ui.Named<TextBox>(host, "Credit note CGST"), "66.96");
            TypeAndLeave(Ui.Named<TextBox>(host, "Credit note SGST"), "66.97");
            page.Lines.Single(line => line.Batch == "BEN2511").CreditNow = "1250";
            Snapshot(host, "5-sent-entering-credit");
            await Ui.PressAsync(Ui.Named<Button>(host, "Record credit note"));
            Assert.True(string.IsNullOrEmpty(page.ErrorMessage), page.ErrorMessage);
            await (page.ClaimLoading ?? Task.CompletedTask);
            await Ui.SettleAsync();
            Assert.Equal(ExpiryClaimStatus.PartCredited, page.Claim!.Status);
            Assert.Contains("Still owed ₹710.11", page.ClaimBalance);
            ExpiryCreditRow credit = Assert.Single(page.Credits);
            Assert.Equal(("KMA/CN/88", "Adjusted against purchases"), (credit.Number, credit.Settlement));
            Assert.Contains("Taxable 1,116.07 · CGST 66.96 · SGST 66.97", credit.Gst);

            // 7. The supplier ledger on Accounts moved by the credit note.
            var accounts = new WholesaleAccountsPageViewModel(scopes, session, null!, new TabularExportService(), new BillingDeskState());
            await accounts.LoadAsync();
            accounts.SelectedSupplier = accounts.Suppliers.Single(item => item.Id == supplier.Id);
            await Ui.UntilAsync(() => accounts.SupplierLedger.Any(row => row.EntryType == LedgerEntryTypes.ExpiryClaimCredit), "the credit note in the supplier ledger");
            LedgerRow posted = accounts.SupplierLedger.Single(row => row.EntryType == LedgerEntryTypes.ExpiryClaimCredit);
            Assert.Equal(("KMA/CN/88", 1250m, payableBefore - 1250m), (posted.ReferenceNo, posted.Debit, posted.Balance));
            output.WriteLine(string.Join("\n", accounts.SupplierLedger.Select(row => $"{row.EntryType} {row.ReferenceNo} Dr {row.Debit} Cr {row.Credit} = {row.Balance}")));

            // 8. The distributor rejects the tablets (below minimum shelf life): the pharmacist chooses to write them off.
            DataGrid lines = Ui.Named<DataGrid>(host, "Claim lines");
            lines.SelectedItem = page.Lines.Single(line => line.Batch == "PAN0226");
            await Ui.SettleAsync();
            Assert.Equal("30", page.RejectQuantity);
            TypeAndLeave(Ui.Named<TextBox>(host, "Rejection reason"), "Less than 3 months of shelf life when received");
            await Ui.PressAsync(Ui.Named<Button>(host, "Rejected: write off"));
            Assert.True(string.IsNullOrEmpty(page.ErrorMessage), page.ErrorMessage);
            Assert.Contains("will be written off", Assert.Single(asks.Questions));
            await (page.ClaimLoading ?? Task.CompletedTask);
            await Ui.SettleAsync();
            ExpiryClaimLineRow rejected = page.Lines.Single(line => line.Batch == "PAN0226");
            Assert.StartsWith("Rejected · written off by Master Admin on ", rejected.State);
            // The syrup's other 150 (its GST) is still owed: the claim stays part credited, ageing.
            Assert.Equal(ExpiryClaimStatus.PartCredited, page.Claim!.Status);
            Assert.Contains("Still owed ₹150.00", page.ClaimBalance);
            Assert.Equal("150.00", Assert.Single(page.OpenClaims).Balance);
            Snapshot(host, "6-after-rejection");
            host.Close();
            // Nothing moved twice: the tablets left stock once, when held.
            Assert.Equal(0m, await database.Context.StockMovements.Where(movement => movement.BatchId == tabletRow.Row.BatchId).SumAsync(movement => movement.QuantityChange));

            // 9. Both reports on the Reports page.
            var reports = new ReportsPageViewModel(scopes, picker, new TabularExportService(), new NoDialog(), new NoViewer(), new DataRetentionSettingsStore(database.Storage));
            Window reportHost = Host(reports, mainWindow);
            await reports.LoadAsync();
            await Ui.SettleAsync();
            async Task<string> ReportAsync(string name)
            {
                await Ui.PressAsync(Ui.Single<Button>(reportHost, b => b.IsVisible && b.DataContext is ReportPanelItem item && item.Name == name));
                await Ui.UntilAsync(() => reports.SummaryReport == name, name);
                await Ui.SettleAsync();
                System.Data.DataView table = reports.Table!;
                return string.Join(" | ", table.Table!.Columns.Cast<System.Data.DataColumn>().Select(column => column.ColumnName)) + "\n"
                    + string.Join("\n", table.Cast<System.Data.DataRowView>().Select(row => string.Join(" | ", row.Row.ItemArray)));
            }
            string ageing = await ReportAsync("Pending expiry claims ageing");
            Snapshot(reportHost, "7-report-ageing");
            string recovery = await ReportAsync("Expiry claim recovery by distributor");
            Snapshot(reportHost, "8-report-recovery");
            output.WriteLine(ageing);
            output.WriteLine(recovery);
            reportHost.Close();
            Assert.Contains("Kashmir Medical Agencies | 1 | 1960.11 | 1250.00 | 150.00 | 0 | 0.00", ageing);
            Assert.Contains("Kashmir Medical Agencies | 1 | 1960.11 | 1250.00 | 63.8% | 560.11 | 0.00 | 150.00", recovery);
        });
    }
}
