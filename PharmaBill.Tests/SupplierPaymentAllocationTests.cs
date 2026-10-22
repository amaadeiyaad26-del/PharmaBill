using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// Supplier payments settle the oldest bills first unless the pharmacist points them at particular bills; Reports,
// Purchase history and the payment screen all read the same balances.
public sealed class SupplierPaymentAllocationTests
{
    [Fact]
    public async Task APaymentPointedAtABill_SettlesThatBill_AndTheRestGoesOldestFirst()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SeedAsync(database);
        var accounts = new WholesaleAccountsService(database.CreateUnitOfWork());

        // ₹50 for the newest bill C, as the supplier asked.
        await accounts.RecordSupplierPaymentAsync(shop.Supplier.Id, 50m, "UPI", "UTR-1", null, null, null, shop.UserId, [new SupplierBillPayment(shop.C.Id, 50m)]);
        Assert.Equal((100m, 200m, 0m), await DueAsync(database, shop));

        // ₹120 more: ₹20 pointed at B, the other ₹100 settles the oldest due, A.
        await accounts.RecordSupplierPaymentAsync(shop.Supplier.Id, 120m, "Cash", null, null, null, null, shop.UserId, [new SupplierBillPayment(shop.B.Id, 20m)]);
        Assert.Equal((0m, 180m, 0m), await DueAsync(database, shop));

        // Without pointing it anywhere, a payment settles the oldest remaining due: B.
        await accounts.RecordSupplierPaymentAsync(shop.Supplier.Id, 30m, "Cash", null, null, null, null, shop.UserId);
        Assert.Equal((0m, 150m, 0m), await DueAsync(database, shop));

        IReadOnlyList<OpenSupplierBill> open = await new SupplierBillBalanceService(database.Context).GetOpenBillsAsync(shop.Supplier.Id);
        Assert.Equal(["B-200"], open.Select(bill => bill.BillNo).ToArray());
        Assert.Equal(1, await database.Context.SupplierPaymentAllocations.CountAsync(item => item.PurchaseInvoiceId == shop.C.Id));
    }

    [Fact]
    public async Task PointingAPaymentWrongly_IsRefused_AndNothingIsSaved()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SeedAsync(database);
        var accounts = new WholesaleAccountsService(database.CreateUnitOfWork());
        var other = new Supplier { Name = "Valley Medical Agencies" };
        var otherBill = new PurchaseInvoice { SupplierId = other.Id, InvoiceNo = "V-1", InvoiceDate = new DateOnly(2026, 10, 2), Status = PurchaseInvoice.PostedStatus, TotalAmount = 40m };
        database.Context.AddRange(other, otherBill, new SupplierLedgerEntry { SupplierId = other.Id, EntryAtUtc = otherBill.InvoiceDate.StartOfDayUtc(), EntryType = LedgerEntryTypes.PurchaseBill, ReferenceId = otherBill.Id, ReferenceNo = "V-1", Credit = 40m });
        await database.Context.SaveChangesAsync();
        Guid otherSuppliersBill = otherBill.Id;

        async Task<string> Refused(decimal amount, params SupplierBillPayment[] bills) =>
            (await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.RecordSupplierPaymentAsync(shop.Supplier.Id, amount, "Cash", null, null, null, null, shop.UserId, bills))).Message;

        Assert.Contains("more than that", await Refused(500m, new SupplierBillPayment(shop.C.Id, 60m)));
        Assert.Contains("more than the payment", await Refused(40m, new SupplierBillPayment(shop.A.Id, 30m), new SupplierBillPayment(shop.C.Id, 20m)));
        Assert.Contains("not an unpaid bill of", await Refused(10m, new SupplierBillPayment(otherSuppliersBill, 10m)));
        Assert.Contains("only once", await Refused(40m, new SupplierBillPayment(shop.A.Id, 10m), new SupplierBillPayment(shop.A.Id, 10m)));
        Assert.Equal(0, await database.Context.SupplierLedgerEntries.CountAsync(entry => entry.SupplierId == shop.Supplier.Id && entry.Debit > 0m));
        Assert.Equal(0, await database.Context.SupplierPaymentAllocations.CountAsync());
    }

    [Fact]
    public void PaymentScreen_PaySpecificBills_ThenPurchaseHistoryShowsTheRealStatus()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            Shop shop = await SeedAsync(database);
            ServiceProvider services = new ServiceCollection()
                .AddSingleton(database.Context)
                .AddScoped(_ => new WholesaleAccountsService(database.CreateUnitOfWork()))
                .BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(await database.Context.AppUsers.FirstAsync(user => user.Id == shop.UserId));
            var page = new WholesaleAccountsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, null!, new TabularExportService(), new BillingDeskState());
            await page.LoadAsync();

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1500, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(WholesaleAccountsPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            Ui.Tab(host, "🏢 Supplier Ledgers & Payables");
            await Ui.SettleAsync();

            ComboBox suppliers = Ui.Single<ComboBox>(host, box => box.ItemsSource == page.Suppliers && box.DisplayMemberPath == "Name" && box.MinHeight == 32);
            suppliers.SelectedItem = page.Suppliers.Single(supplier => supplier.Id == shop.Supplier.Id);
            await Ui.UntilAsync(() => page.OpenBills.Count == 3, "the supplier's unpaid bills");
            Assert.Equal("This payment settles the oldest unpaid bills first.", page.BillAllocationSummary);

            // Point the payment at the newest bill, C.
            Ui.Single<CheckBox>(host, box => System.Windows.Automation.AutomationProperties.GetName(box) == "Pay specific bills").IsChecked = true;
            await Ui.SettleAsync();
            Ui.Single<CheckBox>(host, box => System.Windows.Automation.AutomationProperties.GetName(box) == "C-50").IsChecked = true;
            await Ui.SettleAsync();
            Assert.Equal("50.00", page.SupplierPaymentAmount);
            Assert.StartsWith("₹50.00 against 1 bill.", page.BillAllocationSummary);
            await Ui.PressAsync(Ui.Single<Button>(host, b => b.Command == page.RecordSupplierPaymentCommand));
            await Ui.UntilAsync(() => page.StatusMessage == "Supplier payment (outward) saved." && page.OpenBills.Count == 2, "the payment to save");
            Assert.Equal(string.Empty, page.ErrorMessage);
            Assert.Equal(["A-100", "B-200"], page.OpenBills.Select(bill => bill.Bill.BillNo).ToArray());
            Assert.False(page.PayAgainstBills);
            host.Close();

            // Purchase history shows each bill's real status, not "Unpaid" for every posted bill.
            var history = new PurchaseInvoiceHistoryViewModel(services.GetRequiredService<IServiceScopeFactory>(), null!);
            var window = new PurchaseInvoiceHistoryWindow(history);
            Ui.Prepare(window);
            window.Show();
            await Ui.UntilAsync(() => history.Rows.Any(row => row.InvoiceNo == "C-50"), "purchase history to load");
            Assert.Equal((SupplierBillBalanceService.Paid, (decimal?)0m), Status(history, "C-50"));
            Assert.Equal((SupplierBillBalanceService.Unpaid, (decimal?)100m), Status(history, "A-100"));
            Assert.Equal((SupplierBillBalanceService.Unpaid, (decimal?)200m), Status(history, "B-200"));
            DataGrid grid = Ui.Single<DataGrid>(window, g => g.ItemsSource == history.Rows);
            Assert.Contains("Balance Due", grid.Columns.Select(column => column.Header.ToString()));
            window.Close();
        });
    }

    private static (string, decimal?) Status(PurchaseInvoiceHistoryViewModel history, string billNo)
    {
        PurchaseInvoiceHistoryRow row = history.Rows.Single(item => item.InvoiceNo == billNo);
        return (row.PaymentStatus, row.BalanceDue);
    }

    private static async Task<(decimal A, decimal B, decimal C)> DueAsync(DatabaseTestContext database, Shop shop)
    {
        Dictionary<Guid, decimal> due = await new SupplierBillBalanceService(database.Context).GetBalancesAsync([shop.Supplier.Id]);
        return (due[shop.A.Id], due[shop.B.Id], due[shop.C.Id]);
    }

    private sealed record Shop(Supplier Supplier, PurchaseInvoice A, PurchaseInvoice B, PurchaseInvoice C, Guid UserId);

    // One supplier with three posted bills in October and nothing paid yet.
    private static async Task<Shop> SeedAsync(DatabaseTestContext database)
    {
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);
        var supplier = new Supplier { Name = "Kashmir Pharma Distributors" };
        database.Context.Suppliers.Add(supplier);
        PurchaseInvoice Bill(string no, int day, decimal total)
        {
            var invoice = new PurchaseInvoice { SupplierId = supplier.Id, InvoiceNo = no, InvoiceDate = new DateOnly(2026, 10, day), Status = PurchaseInvoice.PostedStatus, Subtotal = total, TotalAmount = total };
            database.Context.PurchaseInvoices.Add(invoice);
            database.Context.SupplierLedgerEntries.Add(new SupplierLedgerEntry
            {
                SupplierId = supplier.Id, EntryAtUtc = invoice.InvoiceDate.StartOfDayUtc(), EntryType = LedgerEntryTypes.PurchaseBill, ReferenceId = invoice.Id, ReferenceNo = no, Credit = total
            });
            return invoice;
        }
        PurchaseInvoice a = Bill("A-100", 1, 100m), b = Bill("B-200", 5, 200m), c = Bill("C-50", 10, 50m);
        await database.Context.SaveChangesAsync();
        return new Shop(supplier, a, b, c, catalog.Admin.Id);
    }
}
