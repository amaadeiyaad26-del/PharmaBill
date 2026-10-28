using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Accounting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// A retail customer's account: the balance as the headline with its ageing, every bill, payment, return and note, and the
// WhatsApp reminder's state on the button itself (greyed with the reason when there is no phone, fixable in place).
public sealed class CustomerAccountTests
{
    // Asha owes 2,500: a 3,000 credit bill from June (now 90+ days past her 30 days), an 800 balance on an October bill,
    // less a 1,000 UPI payment and a 300 return. A paid October bill shows in her bills and payments. Ravi owes 400 and
    // has no phone.
    private sealed record Shop(AppUser User, Customer Asha, Customer Ravi, Sale June, Sale Paid, Sale October);

    private static DateTime Ist(int month, int day) => new DateTime(2026, month, day, 6, 30, 0, DateTimeKind.Utc); // 12:00 IST

    private static async Task<Shop> SeedAsync(PharmaBillDbContext context)
    {
        var user = new AppUser { UserName = "owner1", DisplayName = "Meera (Owner)", Role = UserRole.Owner, PasswordHash = "x", IsActive = true };
        var asha = new Customer { Name = "Asha Devi", Phone = "9876500001", BuyerType = "Retail", CreditDays = 30, CreditLimit = 5000m };
        var ravi = new Customer { Name = "Ravi Kumar", BuyerType = "Retail", CreditDays = 30 };
        var patient = new Patient { Name = "Asha Devi", Phone = "9876500001" };
        context.AddRange(user, asha, ravi, patient);
        Sale Bill(string no, DateTime at, decimal total, decimal paid) => new() { InvoiceNo = no, SaleAtUtc = at, PatientId = patient.Id, TotalAmount = total, PaidAmount = paid, Subtotal = total };
        Sale june = Bill("INV-0001", Ist(6, 1), 3000m, 0m), october = Bill("INV-0002", Ist(10, 1), 1000m, 200m), paid = Bill("INV-0003", Ist(10, 10), 500m, 500m);
        context.AddRange(june, october, paid);
        context.AddRange(
            new CustomerLedgerEntry { CustomerId = asha.Id, EntryAtUtc = june.SaleAtUtc, EntryType = LedgerEntryTypes.RetailSale, ReferenceId = june.Id, ReferenceNo = june.InvoiceNo, Debit = 3000m, Notes = "Retail credit sale INV-0001" },
            new CustomerLedgerEntry { CustomerId = asha.Id, EntryAtUtc = Ist(9, 1), EntryType = LedgerEntryTypes.OnAccountReceipt, ReferenceNo = "RCPT-1", Credit = 1000m, Notes = "On-account receipt" },
            new CustomerLedgerEntry { CustomerId = asha.Id, EntryAtUtc = october.SaleAtUtc, EntryType = LedgerEntryTypes.RetailSale, ReferenceId = october.Id, ReferenceNo = october.InvoiceNo, Debit = 800m, Notes = "Retail credit sale INV-0002" },
            new CustomerLedgerEntry { CustomerId = asha.Id, EntryAtUtc = Ist(10, 15), EntryType = LedgerEntryTypes.RetailSaleReturn, ReferenceNo = "RN-1", Credit = 300m, Notes = "Retail return against INV-0002" },
            new CustomerLedgerEntry { CustomerId = ravi.Id, EntryAtUtc = Ist(10, 20), EntryType = LedgerEntryTypes.RetailSale, ReferenceNo = "INV-0004", Debit = 400m, Notes = "Retail credit sale INV-0004" },
            new Receipt { CustomerId = asha.Id, ReceiptNo = "RCPT-1", ReceiptAtUtc = Ist(9, 1), Amount = 1000m, PaymentMethod = "UPI", ReferenceNumber = "UPI123", Notes = "On-account receipt" },
            new Receipt { ReceiptNo = "INV-R-3", ReceiptAtUtc = paid.SaleAtUtc, Amount = 500m, PaymentMethod = "Cash", Notes = $"SaleId={paid.Id:D}; InvoiceNo={paid.InvoiceNo}" },
            new ReturnNote { ReturnNo = "RN-1", SourceType = "Sale", SourceId = october.Id, ReturnAtUtc = Ist(10, 15), TotalAmount = 300m, Reason = "Wrong strength" });
        await context.SaveChangesAsync();
        return new Shop(user, asha, ravi, june, paid, october);
    }

    [Fact]
    public async Task Account_HasTheBalance_Ageing_AndEverythingOnIt()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SeedAsync(database.Context);
        var accounts = new CustomerAccountService(database.Context);

        CustomerAccount account = await accounts.GetAccountAsync(shop.Asha.Id, new DateOnly(2026, 10, 21));
        Assert.Equal(2500m, account.Summary.Outstanding);
        Assert.Equal(new AgeingBuckets(800m, 0m, 0m, 1700m), account.Summary.Ageing);
        Assert.Equal(5000m, account.Summary.CreditLimit);
        Assert.Equal([3000m, 2000m, 2800m, 2500m], account.Ledger.Select(line => line.Balance));
        Assert.Equal(["Bill", "Payment", "Bill", "Return"], account.Ledger.Select(line => line.Kind));
        Assert.Equal(["INV-0003", "INV-0002", "INV-0001"], account.Bills.Select(bill => bill.Number));
        Assert.Equal(("UPI", "UPI123"), (account.Payments.Single(payment => payment.ReceiptNo == "RCPT-1").Mode, account.Payments.Single(payment => payment.ReceiptNo == "RCPT-1").Reference));
        Assert.Equal("Paid at the counter on bill INV-0003", account.Payments.Single(payment => payment.ReceiptNo == "INV-R-3").Notes);
        AccountCredit credit = Assert.Single(account.Credits);
        Assert.Equal(("Return", "RN-1", 300m), (credit.Kind, credit.Reference, credit.Amount));

        AccountNote note = await accounts.AddNoteAsync(shop.Asha.Id, "  Pays on the 5th.  ", shop.User);
        Assert.Equal(("Meera (Owner)", "Pays on the 5th."), (note.Author, note.Text));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.AddNoteAsync(shop.Asha.Id, "   ", shop.User));
        Assert.Single((await accounts.GetAccountAsync(shop.Asha.Id, new DateOnly(2026, 10, 21))).Notes);

        IReadOnlyList<CustomerAgeing> dues = await accounts.GetDuesAsync(new DateOnly(2026, 10, 21));
        Assert.Equal([("Asha Devi", 2500m), ("Ravi Kumar", 400m)], dues.Select(due => (due.CustomerName, due.Total)));
    }

    [Fact]
    public async Task Phone_IsTenDigits_AndNeverAnotherCustomers()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await SeedAsync(database.Context);
        var accounts = new CustomerAccountService(database.Context);
        Assert.Contains("10-digit", (await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SetPhoneAsync(shop.Ravi.Id, "12345", shop.User.Id))).Message);
        Assert.Contains("Asha Devi", (await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SetPhoneAsync(shop.Ravi.Id, "98765 00001", shop.User.Id))).Message);
        Assert.Equal("9876500002", await accounts.SetPhoneAsync(shop.Ravi.Id, "+91 98765 00002", shop.User.Id));
        Assert.Contains("(none) -> 9876500002", (await database.Context.AuditLogs.SingleAsync(log => log.Action == "CustomerPhoneSet")).Details);
    }

    private sealed class RecordingInvoices : IInvoicePrintService
    {
        public List<string> Calls { get; } = [];
        public Task<string> PrintAsync(Guid saleId, InvoiceTemplateType? templateOverride = null, bool? silentOverride = null, CancellationToken cancellationToken = default)
        {
            Calls.Add("print " + saleId);
            return Task.FromResult("printed");
        }
        public Task<string> PreviewAsync(Guid saleId, InvoiceTemplateType template = InvoiceTemplateType.StandardA4, CancellationToken cancellationToken = default)
        {
            Calls.Add("preview " + saleId);
            return Task.FromResult("preview.pdf");
        }
        public Task<string?> PickFormatAndPrintAsync(Guid saleId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    [Fact]
    public void AccountView_ThroughTheScreen()
    {
        Ui.Run(async () =>
        {
            using BindingErrorWatch bindings = BindingErrorWatch.Start();
            await using var database = await DatabaseTestContext.CreateAsync();
            Shop shop = await SeedAsync(database.Context);
            var invoices = new RecordingInvoices();
            ServiceProvider services = new ServiceCollection()
                .AddScoped(_ => database.OpenContext())
                .AddScoped(sp => new WholesaleAccountsService(new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>())))
                .AddSingleton<IInvoicePrintService>(invoices)
                .BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(shop.User);
            var page = new WholesaleAccountsPageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, null!, new TabularExportService(), new BillingDeskState());
            var opened = new List<string>();
            page.OpenUrl = opened.Add;
            await page.LoadAsync();

            var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
            BindingSafetyTests.Retire(mainWindow);
            var host = new Window { Width = 1366, Height = 1000 };
            Ui.Prepare(host);
            host.Show();
            host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(WholesaleAccountsPageViewModel))], Resources = mainWindow.Resources };
            await Ui.SettleAsync();
            Ui.Tab(host, "👤 Retail Customer Accounts & Dues");
            await Ui.SettleAsync();

            // Nothing selected: it says what to do. The dues list (from the ledger at the retail desk) has both customers.
            Assert.Contains(Texts(host), text => text.StartsWith("Select a customer above to open their account"));
            Assert.Equal(["Asha Devi", "Ravi Kumar"], page.Dues.Select(due => due.CustomerName));
            Button raviWhatsApp = Ui.Named<Button>(host, "WhatsApp reminder for Ravi Kumar");
            Assert.False(raviWhatsApp.IsEnabled);
            Assert.Equal("No phone number for Ravi Kumar. Open the account to add one.", raviWhatsApp.ToolTip);

            // Asha's row opens her account: the balance is the headline, with its ageing and credit limit.
            Ui.Named<DataGrid>(host, "Customers with dues").SelectedItem = page.Dues[0];
            await page.AccountLoading;
            await Ui.SettleAsync();
            TextBlock balance = Ui.Named<TextBlock>(host, "Outstanding balance");
            Assert.Equal("₹2,500.00", balance.Text);
            Assert.Equal(36, balance.FontSize);
            Assert.Equal("₹1,700.00", Ui.Named<TextBlock>(host, "Ageing 90+").Text);
            Assert.Equal("₹800.00", Ui.Named<TextBlock>(host, "Ageing 0-30").Text);
            Assert.Equal("Credit limit ₹5,000.00 · ₹2,500.00 available", Ui.Named<TextBlock>(host, "Credit limit").Text);

            // Ledger: dates in IST, newest first, running balance; the date range narrows it.
            DataGrid ledger = Ui.Named<DataGrid>(host, "Account ledger");
            Assert.Equal("Date (IST)", ledger.Columns[0].Header);
            Assert.Equal("Balance", ledger.Columns[^1].Header);
            Assert.Equal(["15-Oct-2026  12:00 PM", "01-Oct-2026  12:00 PM", "01-Sep-2026  12:00 PM", "01-Jun-2026  12:00 PM"], page.AccountLedger.Select(row => row.DateText));
            Assert.Equal("2,500.00", page.AccountLedger[0].BalanceText);
            page.AccountFrom = new DateTime(2026, 10, 1);
            Assert.Equal(2, page.AccountLedger.Count);
            await Ui.PressAsync(Ui.Button(host, "All dates"));
            Assert.Equal(4, page.AccountLedger.Count);

            // WhatsApp from the account.
            Button reminder = Ui.Button(host, "WhatsApp reminder");
            Assert.True(reminder.IsEnabled);
            await Ui.PressAsync(reminder);
            Assert.StartsWith("https://wa.me/919876500001?text=", Assert.Single(opened));

            // Bills: open and reprint go to the retail invoice service.
            Ui.Tab(host, "Bills");
            await Ui.SettleAsync();
            await Ui.PressAsync(Ui.Named<Button>(host, "Open INV-0001"));
            await Ui.PressAsync(Ui.Named<Button>(host, "Reprint INV-0001"));
            Assert.Equal(["preview " + shop.June.Id, "print " + shop.June.Id], invoices.Calls);

            // Notes: who wrote it and when.
            Ui.Tab(host, "Notes");
            await Ui.SettleAsync();
            Ui.Type(Ui.Named<TextBox>(host, "New note"), "Pays on the 5th of each month.");
            await Ui.PressAsync(Ui.Button(host, "Add note"));
            Assert.Equal("Meera (Owner) · 21-Oct-2026  02:00 PM", Assert.Single(page.AccountNotes).Heading);

            // Ravi: no phone, so the reminder is greyed with the reason; the number is added right there.
            page.SelectedCustomer = page.Customers.Single(customer => customer.Id == shop.Ravi.Id);
            await page.AccountLoading;
            await Ui.SettleAsync();
            Assert.False(reminder.IsEnabled);
            Assert.Equal("No phone number for Ravi Kumar. Add one to send a reminder.", reminder.ToolTip);
            Assert.Equal("No phone number", Ui.Named<TextBlock>(host, "Customer phone").Text);
            Ui.Tab(host, "Returns & credit notes");
            await Ui.SettleAsync();
            Assert.Contains(Texts(host), text => text.StartsWith("Nothing credited back to this customer."));
            await Ui.PressAsync(Ui.Named<Button>(host, "Add phone number"));
            Ui.Type(Ui.Named<TextBox>(host, "New phone number"), "12345");
            await Ui.PressAsync(Ui.Button(host, "Save phone"));
            Assert.Equal("Enter a 10-digit mobile number.", Ui.Named<TextBlock>(host, "Phone problem").Text);
            Ui.Type(Ui.Named<TextBox>(host, "New phone number"), "98765 00002");
            await Ui.PressAsync(Ui.Button(host, "Save phone"));
            await Ui.UntilAsync(() => reminder.IsEnabled, "the reminder to be available once the phone is saved");
            Assert.Equal("9876500002", Ui.Named<TextBlock>(host, "Customer phone").Text);
            Assert.True(page.Dues.Single(due => due.CustomerName == "Ravi Kumar").HasPhone);

            // Receive Payment: a problem shows under it, not at the top of the page; a payment updates the headline.
            Ui.Type(Ui.Named<TextBox>(host, "Amount received"), "abc");
            await Ui.PressAsync(Ui.Button(host, "Receive Payment"));
            Assert.Equal("Enter the amount received, for example 500.", Ui.Named<TextBlock>(host, "Payment problem").Text);
            Ui.Type(Ui.Named<TextBox>(host, "Amount received"), "150");
            await Ui.PressAsync(Ui.Button(host, "Receive Payment"));
            await Ui.UntilAsync(() => balance.Text == "₹250.00", "the balance after the payment");
            Assert.Equal(string.Empty, page.ErrorMessage);
            bindings.AssertNone();
            host.Close();
        });
    }

    private static IEnumerable<string> Texts(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock { IsVisible: true } text && !string.IsNullOrEmpty(text.Text))
            {
                yield return text.Text;
            }
            foreach (string inner in Texts(child))
            {
                yield return inner;
            }
        }
    }
}
