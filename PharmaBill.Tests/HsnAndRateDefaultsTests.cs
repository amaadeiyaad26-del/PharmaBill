using Microsoft.EntityFrameworkCore;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// HSN from what a product is (a classification), GST from the supplier's invoice or the last rate this shop recorded
// (dated, never before the September 2025 cut); the user is asked only when none of these can answer.
public sealed class HsnAndRateDefaultsTests
{
    // ---------- 1. HSN from the dosage form, with the exceptions table ----------

    [Theory]
    [InlineData("Augmentin 625 Duo Tablet", null, "strip of 10 tablets", null, "3004")]
    [InlineData("Ascoril SF Expectorant", null, "bottle of 100 ml Expectorant", null, "3004")]
    [InlineData("Anfoe 4000IU Injection", null, "prefilled syringe of 1 ml Injection", null, "3004")]
    [InlineData("Ketoplast Plaster", null, "packet of 7 transdermal patches", null, "3004")]
    [InlineData("Asthalin Inhaler", null, null, null, "3004")]
    [InlineData("Betadine 7.5% Surgical Scrub", null, "packet of 50 ml Scrub", null, "3004")]
    [InlineData("Arridruff 2% Shampoo", null, "bottle of 100 ml Shampoo", "Ketoconazole (2% w/v)", "3004")]
    [InlineData("Herbal Shine Shampoo", null, null, null, "3305")]
    [InlineData("Azac Soap", null, "packet of 75 gm Soap", null, "3401")]
    [InlineData("Aster-X Mouth Wash", null, "bottle of 150 ml Mouth Wash", null, "3306")]
    [InlineData("Sensodyne Toothpaste", null, null, null, "3306")]
    [InlineData("Benxop Face Wash", null, "tube of 50 gm Face Wash", null, "3304")]
    [InlineData("Johnson Baby Powder", null, null, null, "3307")]
    [InlineData("Crepe Bandage 10 cm", null, null, null, "3005")]
    [InlineData("Fusigen Tulle", null, "packet of 1 Bandage", null, "3005")]
    [InlineData("Vicryl 2-0 Suture", null, null, null, "3006")]
    [InlineData("Dispovan Syringe 5 ml", null, null, null, "9018")]
    [InlineData("Dr Morepen Glucometer", null, null, null, "9027")]
    [InlineData("Digital Thermometer", null, null, null, "9025")]
    [InlineData("Whey Protein Powder 1 kg", null, null, null, "2106")]
    [InlineData("Manforce Condoms 10s", null, null, null, "4014")]
    [InlineData("Prega News Kit", null, null, null, "3822")]
    [InlineData("Adacel Vaccine", null, "vial of 0.5 ml Injection", null, "3002")]
    [InlineData("Albumin Grifols 20% Infusion", null, "packet of 100 ml Infusion", null, "3002")]
    [InlineData("Ovral L Tablet", null, "strip of 21 tablets", null, "3006")]
    [InlineData("i-pill Tablet", null, null, null, "3006")]
    public void Classify_ByDosageFormAndTheExceptionLists(string name, string? form, string? pack, string? composition, string expected)
    {
        HsnSuggestion? suggestion = HsnClassifier.Classify(name, form, pack, composition);
        Assert.NotNull(suggestion);
        Assert.Equal(expected, suggestion!.Code);
        Assert.Contains(expected, suggestion.Reason);
    }

    [Theory]
    [InlineData("Lirox Powder Delicious Orange", null, "jar of 180 gm Husk")]
    [InlineData("Gift voucher", null, null)]
    [InlineData("", null, null)]
    public void Classify_ReturnsNothing_WhenNothingSaysWhatTheProductIs(string name, string? form, string? pack) =>
        Assert.Null(HsnClassifier.Classify(name, form, pack));

    [Fact]
    public void Classify_ExplainsWhy_AndNeverCarriesARate()
    {
        HsnSuggestion tablet = HsnClassifier.Classify("Dolo 650 Tablet")!;
        Assert.Equal("Tablet → 3004, medicaments in measured doses or retail packs", tablet.Reason);
        Assert.True(tablet.IsMedicament);
        // A classification only: the suggestion has no rate in it at all.
        Assert.DoesNotContain(typeof(HsnSuggestion).GetProperties(), property => property.Name.Contains("Rate") || property.Name.Contains("Gst"));
        Assert.Equal("tablets", HsnClassifier.PackForm("strip of 10 tablets"));
        Assert.Equal("Syrup", HsnClassifier.PackForm("bottle of 60 ml Syrup"));
    }

    // ---------- GST rates ----------

    [Fact]
    public void GstRates_KnowTheSlabsAndTheCut()
    {
        Assert.True(GstRates.IsStandardSlab(5m));
        Assert.True(GstRates.IsStandardSlab(12m));
        Assert.False(GstRates.IsStandardSlab(2.5m));
        Assert.False(GstRates.IsStandardSlab(6m));
        Assert.True(GstRates.IsBeforeRateCut(new DateOnly(2025, 9, 21)));
        Assert.False(GstRates.IsBeforeRateCut(new DateOnly(2025, 9, 22)));
    }

    // ---------- 2. Each invoice line's own HSN and GST ----------

    [Theory]
    [InlineData("Paracetamol 500 30049099 PCT2407 06/27 20 180.00 5% 3600.00", "30049099", 5)]
    [InlineData("Cetrimide Cream 3004 CTR2501 08/27 10 45.00 6% 6% 450.00", "3004", 12)]
    [InlineData("Dolo 650 30049011 DL01 08/27 10 20.00 2.5% 2.5% 200.00", "30049011", 5)]
    [InlineData("Azac Soap 34011190 18 AZ01 08/28 5 50.00 250.00", "34011190", 18)]
    [InlineData("Dolo 650 DL01 08/27 10 20.00 200.00", null, null)]
    public void ReadRow_TakesTheRowsOwnHsnAndRate(string row, string? hsn, int? rate)
    {
        (string? readHsn, decimal? readRate) = InvoiceLineTax.ReadRow(row);
        Assert.Equal(hsn, readHsn);
        Assert.Equal(rate, (int?)readRate);
    }

    [Fact]
    public void Attach_NeverSpreadsOneRateAcrossAMixedRateInvoice()
    {
        ExtractedPurchaseLine Line(string name, string batch) => new(name, batch, "06/2027", 10m, 0m, 30m, 20m, 99m, 200m);
        string[] rows =
        [
            "Paracetamol 500 30049099 PCT2407 06/27 10 20.00 5% 200.00",
            "Cough Syrup 30049011 CS0001 06/27 10 20.00 12% 200.00",
            "Mystery Item MY0001 06/27 10 20.00 200.00",
        ];
        const string mixed = "CGST @ 2.5% SGST @ 2.5% CGST @ 6% SGST @ 6%";
        List<ExtractedPurchaseLine> lines = InvoiceLineTax.Attach([Line("Paracetamol 500", "PCT2407"), Line("Cough Syrup", "CS0001"), Line("Mystery Item", "MY0001")], rows, mixed);
        Assert.Equal((5m, "30049099"), (lines[0].Gst, lines[0].HsnCode));
        Assert.Equal((12m, "30049011"), (lines[1].Gst, lines[1].HsnCode));
        Assert.Equal((0m, (string?)null), (lines[2].Gst, lines[2].HsnCode));

        // An invoice that declares a single slab: a line without its own rate takes that one.
        List<ExtractedPurchaseLine> single = InvoiceLineTax.Attach([Line("Mystery Item", "MY0001")], rows, "CGST @ 2.5% SGST @ 2.5%");
        Assert.Equal(5m, single[0].Gst);
        Assert.Equal([5m, 12m], InvoiceLineTax.InvoiceSlabs(mixed));
        Assert.Equal(5m, InvoiceLineTax.TotalRate(2.5m));
        Assert.Equal(12m, InvoiceLineTax.TotalRate(6m));
        Assert.Equal(18m, InvoiceLineTax.TotalRate(18m));
        Assert.Null(InvoiceLineTax.NormalizeHsn("2026"));
        Assert.Equal("30049099", InvoiceLineTax.NormalizeHsn("3004 90 99"));
    }

    [Fact]
    public void OfflineScan_OfASingleRateInvoice_StillGivesEveryLineThatRate()
    {
        var invoice = InvoiceTextParser.Parse(SingleRateInvoice, "sample");
        Assert.NotEmpty(invoice.Items);
        Assert.All(invoice.Items, line => Assert.Equal(5m, line.Gst));
    }

    [Fact]
    public void AiScan_ReadsEachRowsHsn_AndTurnsAHalfRateIntoTheSlab()
    {
        const string json = """
            {"supplier":"Mixed Pharma","invoiceNo":"MX-1","invoiceDate":"2026-10-20","items":[
              {"itemName":"Paracetamol 500 Tablet","hsn":"3004 90 99","batch":"PCT2407","expiry":"06/2027","quantity":10,"rate":20,"gst":2.5,"amount":200},
              {"itemName":"Azac Soap","hsn":"34011190","batch":"AZ01","expiry":"06/2028","quantity":5,"rate":50,"gst":18,"amount":250},
              {"itemName":"Unknown Thing","hsn":"","batch":"UN01","expiry":"06/2028","quantity":1,"rate":10,"gst":0,"amount":10}]}
            """;
        var invoice = GeminiPurchaseImportService.NormalizeInvoice(GeminiPurchaseImportService.DeserializeInvoice(json));
        Assert.Equal(("30049099", 5m), (invoice.Items[0].HsnCode, invoice.Items[0].Gst));
        Assert.Equal(("34011190", 18m), (invoice.Items[1].HsnCode, invoice.Items[1].Gst));
        Assert.Equal(((string?)null, 0m), (invoice.Items[2].HsnCode, invoice.Items[2].Gst));
    }

    // ---------- 3. "Same as last time", dated, never before the cut ----------

    [Fact]
    public async Task LastRecordedRate_IsTheMostRecent_Dated_AndNeverFromBeforeTheCut()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var context = database.Context;
        var salt = new CatalogMedicine { SourceId = "S1", Name = "Calpol 500 Tablet", CompositionKey = "paracetamol 500mg" };
        var sibling = new CatalogMedicine { SourceId = "S2", Name = "Crocin 500 Tablet", CompositionKey = "paracetamol 500mg" };
        var supplier = new Supplier { Name = "Sup" };
        context.AddRange(salt, sibling, supplier);
        var calpol = new Drug { Name = "Calpol 500", IsActive = true, CatalogMedicineId = salt.Id, CreatedAtUtc = new DateTime(2025, 1, 5) };
        var crocin = new Drug { Name = "Crocin 500", IsActive = true, CatalogMedicineId = sibling.Id, CreatedAtUtc = new DateTime(2025, 1, 5) };
        context.AddRange(calpol, crocin);
        await context.SaveChangesAsync();

        async Task BuyAsync(Drug drug, DateOnly on, decimal rate)
        {
            var invoice = new PurchaseInvoice { SupplierId = supplier.Id, InvoiceNo = "INV-" + on.ToString("yyyyMMdd") + rate, InvoiceDate = on };
            context.Add(invoice);
            context.Add(new PurchaseItem { PurchaseInvoiceId = invoice.Id, DrugId = drug.Id, BatchNo = "B", Quantity = 1m, UnitPrice = 10m, TaxRate = rate });
            await context.SaveChangesAsync();
        }
        var taxes = new ProductTaxService(context);

        // Only a purchase from before the cut: nothing is offered.
        await BuyAsync(calpol, new DateOnly(2025, 8, 1), 12m);
        Assert.Null(await taxes.LastRecordedRateAsync(calpol.Id, null));

        // A later purchase of the same salt after the cut: offered, naming where and when.
        await BuyAsync(crocin, new DateOnly(2025, 10, 3), 5m);
        RememberedRate same = (await taxes.LastRecordedRateAsync(calpol.Id, null))!;
        Assert.Equal((5m, new DateOnly(2025, 10, 3)), (same.Rate, same.RecordedOn));
        Assert.Equal("Same as last time: 5%, from supplier invoice INV-202510035 for Crocin 500 (same salt), 03-Oct-2025", same.Label);

        // The most recent record decides, even when it is a half rate (2.5% is not a slab): then nothing is offered.
        await BuyAsync(calpol, new DateOnly(2025, 11, 1), 2.5m);
        Assert.Null(await taxes.LastRecordedRateAsync(calpol.Id, null));
        await BuyAsync(calpol, new DateOnly(2025, 12, 1), 5m);
        Assert.Equal("supplier invoice INV-202512015", (await taxes.LastRecordedRateAsync(calpol.Id, null))!.From);

        // A medicine with no history of its own or its salt: nothing.
        var lone = new Drug { Name = "Lone Tablet", IsActive = true };
        context.Add(lone);
        await context.SaveChangesAsync();
        Assert.Null(await taxes.LastRecordedRateAsync(lone.Id, null));
    }

    // ---------- 5. Ask only when nothing can answer ----------

    private sealed class RecordingPrompt : IPromptService
    {
        public List<(string Name, string? Hsn, decimal? Rate, ProductTaxHints? Hints)> Asked { get; } = [];
        public ProductTaxPromptResult? Answer { get; set; }
        public string? AskText(string title, string message, string label, string? initialText = null) => null;
        public ProductTaxPromptResult? AskProductTax(string productName, string? currentHsn, decimal? currentRate, bool currentNilRated, bool newProduct = false, ProductTaxHints? hints = null)
        {
            Asked.Add((productName, currentHsn, currentRate, hints));
            return Answer;
        }
    }

    [Fact]
    public async Task NewMedicine_WithTheRateOnTheForm_IsNotAsked_AndGetsItsHsnFromItsForm()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var uow = new UnitOfWork(database.Context);
        var entitlements = new AllowAll();
        var service = new AddStockService(uow, database.CreatePurchaseService(entitlements, uow), entitlements, new NullLicenseRuntimeGuard());
        var prompt = new RecordingPrompt();
        AddStockInput Input(decimal gst) => new(null, null, "Zincovit Syrup", null, null, "OTC", "B1", BusinessClock.Today.AddYears(1), 30m, 20m, 1m, 0m, 1m, gst,
            Guid.NewGuid(), "INV-1", BusinessClock.Today, null);

        AddStockInput? completed = await NewProductTaxPrompt.CompleteAsync(service, prompt, Input(5m));
        Assert.Empty(prompt.Asked);
        Assert.Equal(("3004", 5m), (completed!.HsnCode, completed.GstRate));

        // No rate on the form and none recorded: asked once, with the classified HSN and its reason filled in.
        prompt.Answer = new ProductTaxPromptResult(new ProductTaxDetails("3004", 5m, false), true);
        completed = await NewProductTaxPrompt.CompleteAsync(service, prompt, Input(0m));
        var asked = Assert.Single(prompt.Asked);
        Assert.Equal("3004", asked.Hints!.Hsn!.Code);
        Assert.StartsWith("Syrup → 3004", asked.Hints.Hsn.Reason);
        Assert.Null(asked.Hints.Rate);
        Assert.Equal(5m, completed!.GstRate);
    }

    [Fact]
    public void ScannedLine_WithHsnAndRate_OrAClearFormAndRate_AsksNothing()
    {
        var prompt = new RecordingPrompt();
        ProductTaxDetails? scanned = PharmaBill.App.ViewModels.PurchasePageViewModel.ScannedLineTax("Brandnew XR", "30049099", 12m, () => prompt);
        ProductTaxDetails? byForm = PharmaBill.App.ViewModels.PurchasePageViewModel.ScannedLineTax("Brandnew 500 Tablet", null, 5m, () => prompt);
        Assert.Empty(prompt.Asked);
        Assert.Equal(("30049099", 12m), (scanned!.HsnCode, scanned.GstRate));
        Assert.Equal(("3004", 5m), (byForm!.HsnCode, byForm.GstRate));

        // A half rate (2.5%) is not accepted as a rate, and nothing at all says what "Brandnew XR" is: asked, HSN kept.
        PharmaBill.App.ViewModels.PurchasePageViewModel.ScannedLineTax("Brandnew XR", "30049099", 2.5m, () => prompt);
        PharmaBill.App.ViewModels.PurchasePageViewModel.ScannedLineTax("Brandnew XR", null, 12m, () => prompt);
        Assert.Equal(2, prompt.Asked.Count);
        Assert.Equal(("30049099", (decimal?)null), (prompt.Asked[0].Hsn, prompt.Asked[0].Rate));
        Assert.Equal(((string?)null, (decimal?)12m), (prompt.Asked[1].Hsn, prompt.Asked[1].Rate));
    }

    private sealed class AllowAll : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    // ---------- 4. The one-time pass over the catalogue ----------

    [Fact]
    public async Task CatalogPass_Fills3004OnlyWhereTheFormIsClear_AuditsIt_AndRunsOnce()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var context = database.Context;
        var tablet = new Drug { Name = "Dolo 650 Tablet", IsActive = true, GstRate = 5m };
        var soap = new Drug { Name = "Azac Soap", IsActive = true };
        var mystery = new Drug { Name = "Gift voucher", IsActive = true };
        var done = new Drug { Name = "Calpol Syrup", IsActive = true, HsnCode = "30049011", GstRate = 5m };
        context.AddRange(tablet, soap, mystery, done);
        await context.SaveChangesAsync();

        CatalogHsnPassResult result = await new CatalogHsnPass(context).RunOnceAsync();
        Assert.Equal((false, 3, 1, 1, 1), (result.AlreadyRun, result.Examined, result.Filled, result.ExceptionsLeft, result.Unclassified));
        context.ChangeTracker.Clear();
        Assert.Equal("3004", (await context.Drugs.SingleAsync(drug => drug.Id == tablet.Id)).HsnCode);
        Assert.Equal(5m, (await context.Drugs.SingleAsync(drug => drug.Id == tablet.Id)).GstRate);
        Assert.Null((await context.Drugs.SingleAsync(drug => drug.Id == soap.Id)).HsnCode);
        Assert.Equal("30049011", (await context.Drugs.SingleAsync(drug => drug.Id == done.Id)).HsnCode);
        AuditLog audit = await context.AuditLogs.SingleAsync(log => log.Action == "ProductHsnClassified");
        Assert.Contains("Tablet → 3004", audit.Details);
        Assert.Contains("GST rate not changed", audit.Details);
        Assert.Contains("1 set to 3004", (await context.AuditLogs.SingleAsync(log => log.Action == CatalogHsnPass.AuditAction)).Details);

        Assert.True((await new CatalogHsnPass(context).RunOnceAsync()).AlreadyRun);
    }

    private const string SingleRateInvoice = """
        Seller (Wholesaler)
        M/s Example Pharma Distributors
        Invoice No.: KPD/2026-27/0148
        Date: 08/10/2026
        Product
        Paracetamol Tablets IP 500 mg
        Cetirizine Tablets IP 10 mg
        Batch No
        PCT2407
        CET2601
        Mfg / Exp
        07/25 - 06/27
        01/26 - 12/27
        Qty
        20
        80
        Rate (Rs.)
        180.00
        18.50
        Amount (Rs.)
        3600.00
        1480.00
        Taxable value
        Rs. 5,080.00
        CGST @ 2.5%
        SGST @ 2.5%
        Grand total
        Rs. 5,334.00
        Authorised Signatory
        """;
}
