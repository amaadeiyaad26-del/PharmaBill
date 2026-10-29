using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Gst;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// The supplier invoice the owner scanned (Fixtures/Invoices: a fictional six-line bill at 5%, 12% and 18%) with the
// values printed on it. It is read column by column; a reading that does not match its printed totals is refused;
// Gemini failing is said, never hidden; and through the purchase screen it is saved without the HSN dialog.
// Runs alone: one test saves a Gemini key in the test data folder, which every extractor in the process would see.
[Collection(RunsAlone.Name)]
public sealed class ScannedInvoiceFixtureTests
{
    private static readonly string Pdf = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Invoices", "Example_Pharma_Distributors_Invoice.pdf");

    private sealed record ExpectedLine(string Name, string Manufacturer, string Pack, string Hsn, string Batch, string Expiry, decimal Qty, decimal Free, decimal Rate, decimal Amount, decimal Gst);

    private sealed record Expected(string Supplier, string InvoiceNo, string InvoiceDate, decimal Taxable, decimal Cgst, decimal Sgst, decimal RoundOff, decimal GrandTotal, List<ExpectedLine> Lines);

    private static Expected Bill() => JsonSerializer.Deserialize<Expected>(
        File.ReadAllText(Path.ChangeExtension(Pdf, ".expected.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private sealed class NoOcr : IOcrTextRecognizer
    {
        public Task<string> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
        public Task<IReadOnlyList<OcrWordPosition>> RecognizeWordsAsync(byte[] imageBytes, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OcrWordPosition>>([]);
    }

    private static void AssertMatches(Expected bill, ExtractedPurchaseInvoice invoice)
    {
        Assert.Equal(bill.Lines.Count, invoice.Items.Count);
        for (int index = 0; index < bill.Lines.Count; index++)
        {
            ExpectedLine want = bill.Lines[index];
            ExtractedPurchaseLine got = invoice.Items[index];
            Assert.Equal(
                (want.Name, want.Manufacturer, want.Pack, want.Hsn, want.Batch, want.Expiry, want.Qty, want.Free, want.Rate, want.Amount, want.Gst),
                (got.ItemName, got.Manufacturer, got.Pack, got.HsnCode, got.Batch, got.Expiry, got.Quantity, got.Free, got.Rate, got.Amount, got.Gst));
        }
        Assert.Equal(bill.InvoiceNo, invoice.InvoiceNo);
        Assert.Equal(bill.InvoiceDate, invoice.InvoiceDate);
        Assert.Contains(bill.Supplier, invoice.Supplier, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new InvoicePrintedTotals(bill.Taxable, bill.Cgst, bill.Sgst, null, bill.RoundOff, bill.GrandTotal), invoice.Printed);
        Assert.Empty(InvoiceReconciliation.Problems(invoice));
    }

    [Fact]
    public void TheTable_IsReadByItsColumns_ExactlyAsPrinted()
    {
        Expected bill = Bill();
        ExtractedPurchaseInvoice invoice = InvoiceTextParser.ReadTable(InvoiceDocumentExtractor.ReadEmbeddedWords(Pdf), "fixture")
            ?? throw new Xunit.Sdk.XunitException("no item table was found");
        AssertMatches(bill, invoice);
        // Every line carries its own HSN and a real GST rate, so nothing needs asking.
        Assert.All(invoice.Items, line => Assert.True(ProductTax.HasHsn(line.HsnCode) && PharmaBill.Core.Gst.GstRates.IsStandardSlab(line.Gst), line.ItemName));
    }

    [Fact]
    public void TheOldFlatTextReading_OfThisBill_IsRefused_AndSaysWhy()
    {
        // What reached the screen before: rows split from one run of text, rate and free quantity in Amount, GST 0.
        ExtractedPurchaseInvoice flat = InvoiceTextParser.Parse(InvoiceDocumentExtractor.ReadEmbeddedText(Pdf), "fixture");
        IReadOnlyList<string> problems = InvoiceReconciliation.Problems(flat);
        Assert.Contains(problems, problem => problem.Contains("printed taxable value is ₹11,450.00", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("printed GST is ₹1,045.00", StringComparison.Ordinal));
        var refused = Assert.Throws<InvoiceNotReconciledException>(() => InvoiceReconciliation.Ensure(flat, "Read on this PC because no Gemini key is saved"));
        Assert.Contains("was not loaded", refused.Message);
        Assert.Contains("Nothing was put on the purchase grid", refused.Message);
    }

    [Fact]
    public void AReadingMissingALine_OrWithoutPrintedTotals_IsRefused()
    {
        ExtractedPurchaseInvoice invoice = InvoiceTextParser.ReadTable(InvoiceDocumentExtractor.ReadEmbeddedWords(Pdf), "fixture")!;
        ExtractedPurchaseInvoice missingLine = invoice with { Items = invoice.Items.Skip(1).ToList() };
        Assert.Contains(InvoiceReconciliation.Problems(missingLine), problem => problem.Contains("the 5 lines read add up to ₹9,650.00"));

        ExtractedPurchaseInvoice wrongRate = invoice with { Items = invoice.Items.Select((line, index) => index == 3 ? line with { Gst = 5m } : line).ToList() };
        Assert.Contains(InvoiceReconciliation.Problems(wrongRate), problem => problem.Contains("printed GST is ₹1,045.00"));

        ExtractedPurchaseInvoice noTotals = invoice with { Printed = null };
        Assert.Contains(InvoiceReconciliation.Problems(noTotals), problem => problem.Contains("could not be found"));
    }

    [Fact]
    public async Task WithNoGeminiKey_ItSaysItWasReadOnThisPc_AndWhy()
    {
        var extractor = new InvoiceDocumentExtractor(null!, new GeminiApiKeyStore(), new NoOcr());
        new GeminiApiKeyStore().Delete();
        ExtractedPurchaseInvoice invoice = await extractor.ExtractCheckedAsync(Pdf);
        AssertMatches(Bill(), invoice);
        Assert.StartsWith("Read on this PC because no Gemini key is saved; the item table was read column by column", invoice.ReadBy);
    }

    [Fact]
    public async Task WhenGeminiIsBusy_ItSaysSo_AndThatTheBillWasReadOnThisPc()
    {
        var keys = new GeminiApiKeyStore();
        keys.Save("test-key");
        try
        {
            var gemini = new GeminiPurchaseImportService(new HttpClient(new Answer(HttpStatusCode.ServiceUnavailable, "{\"error\":{\"code\":503}}")), keys);
            ExtractedPurchaseInvoice invoice = await new InvoiceDocumentExtractor(gemini, keys, new NoOcr()).ExtractCheckedAsync(Pdf);
            AssertMatches(Bill(), invoice);
            Assert.StartsWith("Read on this PC because Gemini did not answer (HTTP 503, Google's service was busy)", invoice.ReadBy);
        }
        finally
        {
            keys.Delete();
        }
    }

    [Fact]
    public async Task WhenGeminisReadingDoesNotAddUp_ItIsNotUsed_AndThatIsSaid()
    {
        var keys = new GeminiApiKeyStore();
        keys.Save("test-key");
        try
        {
            // Gemini answers, but with one line and a printed total its lines do not reach.
            string json = JsonSerializer.Serialize(new
            {
                supplier = "Example Pharma Distributors", invoiceNo = "EPD/26-27/00418", invoiceDate = "2026-10-26",
                items = new[] { new { itemName = "Paracetamol 650mg Tablet", hsn = "3004", batch = "PC2409", expiry = "08/2028", quantity = 10, free = 1, rate = 180.00, gst = 5, amount = 1800.00 } },
                taxableValue = 11450.00, cgst = 522.50, sgst = 522.50, grandTotal = 12495.00
            });
            string body = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = json } } } } } });
            var gemini = new GeminiPurchaseImportService(new HttpClient(new Answer(HttpStatusCode.OK, body)), keys);
            ExtractedPurchaseInvoice invoice = await new InvoiceDocumentExtractor(gemini, keys, new NoOcr()).ExtractCheckedAsync(Pdf);
            AssertMatches(Bill(), invoice);
            Assert.StartsWith("Read on this PC because Gemini's reading did not match the bill's printed totals (the 1 lines read add up to ₹1,800.00", invoice.ReadBy);
        }
        finally
        {
            keys.Delete();
        }
    }

    private sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class NeverAsked : IPromptService
    {
        public List<string> Asked { get; } = [];
        public string? AskText(string title, string message, string label, string? initialText = null) { Asked.Add(title); return null; }
        public ProductTaxPromptResult? AskProductTax(string productName, string? currentHsn, decimal? currentRate, bool currentNilRated, bool newProduct = false, ProductTaxHints? hints = null)
        {
            Asked.Add("HSN dialog: " + productName);
            return null;
        }
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static (PurchasePageViewModel Page, Window Host, NeverAsked Prompt) Screen(DatabaseTestContext database, E2EHealthSeed.Catalog catalog, IOcrTextRecognizer ocr)
    {
        var prompt = new NeverAsked();
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(database.Storage)
            .AddSingleton(database.BranchSettings)
            .AddSingleton<IEntitlementService>(new Allowed())
            .AddSingleton<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>()
            .AddScoped(_ => database.OpenContext())
            .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
            .AddScoped<NumberSeriesService>()
            .AddScoped<StorageLocationService>()
            .AddScoped<BranchService>()
            .AddScoped<PurchaseService>()
            .AddScoped<AddStockService>()
            .AddScoped<CustomMedicineService>()
            .AddScoped<ProductTaxService>()
            .AddSingleton<IPromptService>(prompt)
            // No Gemini key: the bill is read on this PC, and the screen says so.
            .AddSingleton(_ => new InvoiceDocumentExtractor(null!, null!, ocr))
            .BuildServiceProvider();
        var session = new CurrentSession();
        session.SignIn(catalog.Admin);
        var page = new PurchasePageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, null!, new BackupPageEndToEndTests.AlwaysYes(),
            null!, prompt, null!, null!, null!, ocr, null!);
        var mainWindow = new PharmaBill.App.MainWindow(null!, null!, null!, null!, new BindingSafetyTests.NullScannerService());
        BindingSafetyTests.Retire(mainWindow);
        var host = new Window { Width = 1366, Height = 900 };
        Ui.Prepare(host);
        host.Show();
        host.Content = new ContentControl { Content = page, ContentTemplate = (DataTemplate)mainWindow.Resources[new DataTemplateKey(typeof(PurchasePageViewModel))], Resources = mainWindow.Resources };
        return (page, host, prompt);
    }

    [Fact]
    public void ThroughThePurchaseScreen_TheBillIsSavedAsPrinted_AndTheHsnDialogNeverAppears()
    {
        Ui.Run(async () =>
        {
            // The day the bill was scanned: the suite's own date (21 Oct) is before the bill's (26 Oct).
            using IDisposable scannedOn = PharmaBill.Core.BusinessClock.Use(new PharmaBill.Core.FixedClock(PharmaBill.Core.BusinessClock.FromLocal(new DateTime(2026, 10, 28, 14, 0, 0))));
            Expected bill = Bill();
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            (PurchasePageViewModel page, Window host, NeverAsked prompt) = Screen(database, catalog, new NoOcr());
            await page.LoadAsync();
            await Ui.SettleAsync();

            string scan = Path.Combine(TestAppData.Root, "scan-" + Guid.NewGuid().ToString("N") + ".pdf");
            File.Copy(Pdf, scan);
            await page.ImportDocumentFromPathAsync(scan);
            await Ui.SettleAsync();
            Assert.True(page.StagedItems.Count == 6, page.ErrorMessage + " " + page.ImportStatus);
            Assert.StartsWith("Read on this PC because no Gemini key is saved", page.ImportStatus);
            Assert.Contains("match the bill's printed total of 12495.00", page.BillTotalCheck);

            await Ui.PressAsync(Ui.Button(host, "Import Rows into Table"));
            Assert.Equal(bill.Lines.Select(line => (line.Name, line.Hsn, line.Gst, line.Qty, line.Free, line.Rate)),
                page.Lines.Select(line => (line.MedicineName, line.HsnCode, line.GstRate, line.Quantity, line.FreeQuantity, line.Rate)));
            Assert.Equal((bill.Taxable, bill.Cgst + bill.Sgst, bill.GrandTotal), (page.Subtotal, page.TaxTotal, page.GrandTotal));

            // The thermometer (HSN 9025, chapter 90) prints no expiry and needs none: the cell says so and offers no tick.
            PurchaseLineDraft thermometerLine = page.Lines.Single(line => line.MedicineName == "Digital Thermometer");
            Assert.Equal(string.Empty, thermometerLine.Expiry);
            Assert.True(thermometerLine.IsDurableInstrument);
            Assert.Contains(Ui.Descendants(host).OfType<TextBlock>(), text => text.IsVisible && text.Text == "Instrument: no expiry" && text.DataContext == thermometerLine);
            Assert.DoesNotContain(NoExpiryTicks(host), tick => tick.DataContext == thermometerLine);
            Assert.Equal(new DateTime(2026, 10, 26), page.InvoiceDate.Date);

            // A dressing (HSN 3005) is not an instrument: with its expiry cleared, saving is refused and says what to do.
            PurchaseLineDraft bandageLine = page.Lines.Single(line => line.MedicineName == "Cotton Bandage 10cm x 3m");
            bandageLine.Expiry = string.Empty;
            await Ui.SettleAsync();
            await Ui.PressAsync(Ui.Button(host, "Transfer to Stock / F10"));
            Assert.Contains("'Cotton Bandage 10cm x 3m' needs an expiry date", page.ErrorMessage);
            Assert.Contains("No expiry printed on pack", page.ErrorMessage);
            await using (PharmaBillDbContext none = database.OpenContext())
            {
                Assert.False(await none.PurchaseInvoices.AnyAsync(item => item.InvoiceNo == bill.InvoiceNo));
            }

            // The owner ticks "No expiry printed on pack" on that row: a deliberate choice, and now it saves.
            CheckBox tick = NoExpiryTicks(host).Single(box => box.DataContext == bandageLine);
            tick.IsChecked = true;
            Assert.True(bandageLine.NoExpiryPrinted);
            await Ui.PressAsync(Ui.Button(host, "Transfer to Stock / F10"));
            Assert.Empty(prompt.Asked);
            Assert.True(page.ErrorMessage.Length == 0, page.ErrorMessage);
            await using PharmaBillDbContext check = database.OpenContext();
            Dictionary<string, (DateOnly? Expiry, PharmaBill.Core.Inventory.NoExpiryReason? Reason)> batches = await check.Batches.AsNoTracking()
                .ToDictionaryAsync(batch => batch.BatchNo, batch => (batch.ExpiryDate, batch.NoExpiryReason));
            Assert.Equal((null, PharmaBill.Core.Inventory.NoExpiryReason.DurableInstrument), batches["DT9902"]);
            Assert.Equal((null, PharmaBill.Core.Inventory.NoExpiryReason.NotPrintedOnPack), batches["CB4410"]);
            Assert.Equal((new DateOnly(2028, 8, 31), null), batches["PC2409"]);
            PurchaseInvoice saved = await check.PurchaseInvoices.AsNoTracking().SingleAsync(item => item.InvoiceNo == bill.InvoiceNo);
            Assert.Equal((bill.Taxable, bill.Cgst + bill.Sgst, bill.GrandTotal), (saved.Subtotal, saved.TaxAmount, saved.TotalAmount));
            Drug thermometer = await check.Drugs.AsNoTracking().SingleAsync(drug => drug.Name == "Digital Thermometer");
            Assert.Equal(("9025", (decimal?)18m), (thermometer.HsnCode, thermometer.GstRate));
            host.Close();
        });
    }

    private static List<CheckBox> NoExpiryTicks(DependencyObject root) =>
        Ui.Descendants(root).OfType<CheckBox>()
            .Where(box => box.IsVisible && System.Windows.Automation.AutomationProperties.GetName(box) == "No expiry printed on pack").ToList();

    [Fact]
    public void ThroughThePurchaseScreen_ABillThatDoesNotAddUp_IsRefused_AndNothingIsStaged()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            // The photo reads as two lines, but the bill's printed taxable value is far more than they add up to.
            var ocr = new LaidOutOcr("""
                Seller (Wholesaler)
                M/s Example Pharma Distributors
                Invoice No.: EPD/26-27/00418
                Date: 26/10/2026
                Product                    HSN   Batch   Exp    Qty  Rate     GST  Amount
                Paracetamol 650mg Tablet   3004  PC2409  08/28  10   180.00   5%   1,800.00
                Azithromycin 500mg Tablet  3004  AZ5512  11/27  50   62.50    5%   3,125.00
                Taxable Value 11,450.00
                CGST 522.50
                SGST 522.50
                Grand Total Rs 12,495.00
                """);
            (PurchasePageViewModel page, Window host, NeverAsked _) = Screen(database, catalog, ocr);
            await page.LoadAsync();
            string photo = Path.Combine(TestAppData.Root, "photo-" + Guid.NewGuid().ToString("N") + ".png");
            ScannedPurchaseEndToEndTests.WritePhoto(photo);
            await page.ImportDocumentFromPathAsync(photo);
            await Ui.SettleAsync();

            Assert.Empty(page.StagedItems);
            Assert.Empty(page.Lines.Where(line => !string.IsNullOrWhiteSpace(line.MedicineName)));
            Assert.Contains("This bill was not loaded", page.ErrorMessage);
            Assert.Contains("add up to ₹4,925.00, but the bill's printed taxable value is ₹11,450.00", page.ErrorMessage);
            Assert.Contains("Read on this PC because no Gemini key is saved", page.ErrorMessage);
            host.Close();
        });
    }
}
