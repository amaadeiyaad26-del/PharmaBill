using System.Data;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using QuestPDF.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace PharmaBill.Tests;

// Credit and debit notes used through the screens: a credit note against a real wholesale invoice and a debit note on
// Wholesale returns, the customer's account (ledger and credits), the Sale return book in Reports exported to Excel.
// Then every master is edited, and each screen still shows what the notes recorded.
[Collection(RunsAlone.Name)]
public sealed class CreditDebitNoteScreenTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.NoteScreens.{Guid.NewGuid():N}");

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

    private sealed class NoDialog : IInvoiceDetailDialogService
    {
        public void Show(InvoiceDetail detail) { }
    }

    private sealed class NoViewer : ISupplierBillViewer
    {
        public void Show(PurchaseBillAttachment bill, string path) { }
    }

    // Typing into a field and moving on: these fields hand their text to the screen when the cursor leaves them.
    private static void TypeAndLeave(TextBox box, string text)
    {
        Ui.Type(box, text);
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
    }

    private static Window Host(object page)
    {
        var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
        BindingSafetyTests.Retire(mainWindow);
        var host = new Window { Width = 1500, Height = 1000 };
        Ui.Prepare(host);
        host.Show();
        host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(page.GetType())], Resources = mainWindow.Resources };
        return host;
    }

    [Fact]
    public void Notes_ThroughTheScreens_StillShowWhatTheyRecorded_AfterTheMastersChange()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Directory.CreateDirectory(_root);
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            var allowed = new Allowed();
            WholesaleInvoiceResult invoice = await database.CreateWholesaleInvoiceService(allowed).SaveAsync(
                new SaveWholesaleInvoiceInput(catalog.BurhanMedicalHall.Id, [new WholesaleInvoiceLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 10m, 0m, 28m)],
                    0m, "Credit", null, false, 90, false, null, null, null, null, null, "Screen test"),
                catalog.Admin.Id, UserRole.Owner);
            var session = new CurrentSession();
            session.SignIn(catalog.Admin);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(session)
                .AddSingleton(new DocumentOutputSettingsStore())
                .AddSingleton<SensitiveAccessService>()
                .AddSingleton<RecordComplianceGate>()
                .AddScoped(_ => database.OpenContext())
                .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
                .AddSingleton<IEntitlementService>(allowed)
                .AddScoped(sp => new WholesaleReturnsService(sp.GetRequiredService<IUnitOfWork>(), allowed))
                .AddScoped(sp => new WholesaleAccountsService(sp.GetRequiredService<IUnitOfWork>()))
                .AddScoped(sp => new ReportsDashboardService(sp.GetRequiredService<PharmaBillDbContext>(), database.CreateBranchService(), new SalesAnalysisService(sp.GetRequiredService<PharmaBillDbContext>())))
                .BuildServiceProvider();

            // 1. Wholesale returns: a credit note against the invoice, then a debit note for the same buyer.
            var returns = new WholesaleReturnsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, new WholesaleReissueHandoff(), null!);
            await returns.LoadAsync();
            Window host = Host(returns);
            await Ui.SettleAsync();
            ComboBox invoices = Ui.Single<ComboBox>(host, box => box.ItemsSource == returns.Invoices);
            invoices.SelectedItem = returns.Invoices.Single(choice => choice.Id == invoice.Invoice.Id);
            await Ui.UntilAsync(() => returns.InvoiceLines.Count == 1, "the invoice's lines");
            TypeAndLeave(Ui.Single<TextBox>(host, box => box.IsVisible && Equals(box.ToolTip, "Credit / debit note number")), "CN-SCREEN-1");
            TypeAndLeave(Ui.Single<TextBox>(host, box => box.IsVisible && Equals(box.ToolTip, "Mandatory reason")), "Damaged in transit");
            returns.InvoiceLines[0].Quantity = 3m; // the "Return qty" cell
            await Ui.PressAsync(Ui.Button(host, "Create credit note"));
            Assert.True(string.IsNullOrEmpty(returns.ErrorMessage), returns.ErrorMessage);

            Ui.Tab(host, "Debit note");
            await Ui.SettleAsync();
            TypeAndLeave(Ui.Single<TextBox>(host, box => box.IsVisible && Equals(box.ToolTip, "Credit / debit note number")), "DN-SCREEN-1");
            TypeAndLeave(Ui.Single<TextBox>(host, box => box.IsVisible && Equals(box.ToolTip, "Mandatory reason")), "Freight recovery");
            TypeAndLeave(Ui.Single<TextBox>(host, box => box.IsVisible && Equals(box.ToolTip, "Debit amount")), "150");
            await Ui.PressAsync(Ui.Button(host, "Create debit note for selected buyer"));
            Assert.True(string.IsNullOrEmpty(returns.ErrorMessage), returns.ErrorMessage);
            host.Close();

            database.Context.ChangeTracker.Clear();
            ReturnNote credit = await database.Context.ReturnNotes.AsNoTracking().SingleAsync(note => note.ReturnNo == "CN-SCREEN-1");
            ReturnNote debit = await database.Context.ReturnNotes.AsNoTracking().SingleAsync(note => note.ReturnNo == "DN-SCREEN-1");
            Assert.Equal((catalog.BurhanMedicalHall.Name, false, false), (credit.BuyerLegalName, credit.IsInterState, credit.ValuesBackfilled));
            Assert.Equal((catalog.BurhanMedicalHall.Gstin, BusinessClock.Today), (debit.BuyerGstin, debit.ReturnDate!.Value));

            // 2. The customer's account: ledger and credits, as the screen shows them.
            var accounts = new WholesaleAccountsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, null!, new TabularExportService(), new BillingDeskState());
            async Task<string> AccountAsync()
            {
                await accounts.LoadAsync();
                Window accountHost = Host(accounts);
                await Ui.SettleAsync();
                Ui.Tab(accountHost, "👤 Retail Customer Accounts & Dues");
                accounts.SelectedCustomer = accounts.Customers.Single(customer => customer.Id == catalog.BurhanMedicalHall.Id);
                await accounts.AccountLoading;
                await Ui.SettleAsync();
                DataGrid ledger = Ui.Named<DataGrid>(accountHost, "Account ledger");
                string ledgerText = string.Join("\n", accounts.AccountLedger.Select(row => $"{row.DateText} {row.Kind} {row.Reference} {row.BalanceText}"));
                Ui.Tab(accountHost, "Returns & credit notes");
                await Ui.SettleAsync();
                Assert.True(Ui.Named<DataGrid>(accountHost, "Customer returns and credit notes").IsVisible);
                accountHost.Close();
                return ledgerText;
            }
            string accountBefore = await AccountAsync();
            output.WriteLine(accountBefore);
            Assert.Contains("CN-SCREEN-1", accountBefore);
            Assert.Contains("DN-SCREEN-1", accountBefore);

            // 3. Reports: the Sale return book, exported to Excel.
            var picker = new ExportTo(_root);
            var reports = new ReportsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), picker, new TabularExportService(), new NoDialog(), new NoViewer(), new DataRetentionSettingsStore(database.Storage));
            async Task<string> ReturnBookAsync()
            {
                Window reportHost = Host(reports);
                await reports.LoadAsync();
                await Ui.SettleAsync();
                await Ui.PressAsync(Ui.Single<Button>(reportHost, b => b.IsVisible && b.DataContext is ReportPanelItem { Name: "Sale return book (credit notes)" }));
                await Ui.UntilAsync(() => reports.SummaryReport == "Sale return book (credit notes)", "the sale return book");
                await Ui.SettleAsync();
                await Ui.PressAsync(Ui.Single<Button>(reportHost, b => b.IsVisible && b.Command == reports.ExportExcelCommand));
                string xlsx = picker.Last!;
                await Ui.UntilAsync(() => File.Exists(xlsx), "the Excel export");
                using var workbook = new XLWorkbook(xlsx);
                string sheet = string.Join("\n", workbook.Worksheets.First().RowsUsed().Select(row => string.Join(" | ", row.CellsUsed().Select(cell => cell.GetString()))));
                File.Delete(xlsx);
                reportHost.Close();
                return sheet;
            }
            string bookBefore = await ReturnBookAsync();
            output.WriteLine(bookBefore);
            Assert.Contains("CN-SCREEN-1", bookBefore);
            Assert.Contains(catalog.BurhanMedicalHall.Name, bookBefore);
            Assert.Contains("Party | Reason | Taxable | GST | Amount", bookBefore);

            // 4. Every master changes: the buyer's name, GSTIN and address, the product's name, HSN and rate.
            database.Context.ChangeTracker.Clear();
            Customer buyer = await database.Context.Customers.SingleAsync(item => item.Id == catalog.BurhanMedicalHall.Id);
            buyer.Name = "Renamed Medical Hall";
            buyer.Gstin = "07AAAAA9999A1Z5";
            buyer.Address = "Elsewhere";
            Drug drug = await database.Context.Drugs.SingleAsync(item => item.Id == catalog.Paracetamol650.Id);
            drug.Name = "Renamed Paracetamol";
            drug.HsnCode = "99999999";
            drug.GstRate = 18m;
            await database.Context.SaveChangesAsync();

            Assert.Equal(bookBefore, await ReturnBookAsync());
            Assert.Equal(accountBefore, await AccountAsync());
        });
    }
}
