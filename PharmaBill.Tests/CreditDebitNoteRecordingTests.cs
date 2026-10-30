using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace PharmaBill.Tests;

// A filed credit or debit note records its own values when it is saved, and every report reads them back. Editing a
// product's GST rate, HSN or name, or a party's GSTIN, name or address, afterwards never changes a note already issued.
[Collection(RunsAlone.Name)]
public sealed class CreditDebitNoteRecordingTests
{
    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private static readonly Allowed Allow = new();

    private sealed record Shop(DatabaseTestContext Database, E2EHealthSeed.Catalog Catalog, ServiceProvider Services);

    private static async Task<Shop> OpenAsync(DatabaseTestContext database)
    {
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);
        ServiceProvider services = new ServiceCollection()
            .AddScoped(_ => database.OpenContext())
            .AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<PharmaBillDbContext>()))
            .BuildServiceProvider();
        return new Shop(database, catalog, services);
    }

    private static ReportsDashboardService Reports(DatabaseTestContext database) =>
        new(database.Context, database.CreateBranchService(), new SalesAnalysisService(database.Context));

    private static string Table(ReportTable table) =>
        string.Join(" | ", table.Columns) + "\n" + string.Join("\n", table.Rows.Select(row => string.Join(" | ", row)));

    /// <summary>Everything the app shows about the notes: both return books, the drug register, the party ledger.</summary>
    private static async Task<string> EverythingAsync(Shop shop, IReadOnlyCollection<Guid> drugIds, Guid? customerId)
    {
        DatabaseTestContext database = shop.Database;
        database.Context.ChangeTracker.Clear();
        DateOnly today = BusinessClock.Today;
        var text = new StringBuilder();
        text.AppendLine(Table(await Reports(database).GetReportAsync(ReportKind.SaleReturnBook, today.AddDays(-1), today)));
        text.AppendLine(Table(await Reports(database).GetReportAsync(ReportKind.PurchaseReturnBook, today.AddDays(-1), today)));
        DrugRecordsReport register = await new DrugRecordsService(shop.Services.GetRequiredService<IServiceScopeFactory>())
            .SearchAsync(new DrugRecordsQuery(drugIds, today.AddDays(-2).StartOfDayUtc(), today.AddDays(1).StartOfDayUtc()), shop.Catalog.Admin.Id);
        foreach (DrugRecordsRow row in register.Rows.Where(row => row.RecordType.Contains("return", StringComparison.OrdinalIgnoreCase)))
        {
            text.AppendLine($"{row.RecordType} {row.DocumentNo} {row.PatientName} | {row.Address} | {row.Phone} | {row.DoctorName} | {row.BatchNo} | {row.Quantity}");
        }
        if (customerId is Guid id)
        {
            CustomerAccount account = await new CustomerAccountService(database.Context).GetAccountAsync(id, today);
            foreach (AccountCredit credit in account.Credits)
            {
                text.AppendLine($"credit {credit.Kind} {credit.Reference} {credit.Amount} {credit.Notes}");
            }
        }
        return text.ToString();
    }

    /// <summary>Every master record a note could have been re-read from, changed.</summary>
    private static async Task ChangeEveryMasterAsync(Shop shop)
    {
        PharmaBillDbContext context = shop.Database.Context;
        context.ChangeTracker.Clear();
        foreach (Drug drug in await context.Drugs.ToListAsync())
        {
            drug.Name += " (renamed)";
            drug.HsnCode = "99999999";
            drug.GstRate = drug.GstRate == 18m ? 5m : 18m;
            drug.Unit = "Changed pack";
        }
        foreach (Customer customer in await context.Customers.ToListAsync())
        {
            customer.Name += " (renamed)";
            customer.Address = "New address";
            customer.Gstin = "07AAAAA9999A1Z5";
            customer.State = "Delhi";
        }
        foreach (Patient patient in await context.Patients.ToListAsync())
        {
            patient.Name += " (renamed)";
            patient.Address = "New patient address";
            patient.Phone = "9000000000";
        }
        foreach (Supplier supplier in await context.Suppliers.ToListAsync())
        {
            supplier.Name += " (renamed)";
            supplier.Address = "New supplier address";
            supplier.Gstin = "27AAAAA8888A1Z5";
        }
        PharmacyProfile profile = await context.PharmacyProfiles.SingleAsync();
        profile.Name += " (renamed)";
        profile.Gstin = "29AAAAA7777A1Z5";
        profile.State = "Karnataka";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static string Recorded(ReturnNote note) =>
        $"{note.ReturnNo} {note.ReturnDate} {note.PharmacyStateCode}/{note.BuyerStateCode} inter={note.IsInterState} {note.BuyerLegalName} | {note.BuyerGstin} | {note.BuyerAddress} | {note.SellerSnapshotJson} | {note.BuyerSnapshotJson}";

    private static string Recorded(SaleReturnItem line) =>
        $"{line.ItemName} {line.HsnCode} {line.Unit} {line.BatchNo} {line.BatchExpiry} {line.UnitPrice} {line.TaxRate} {line.DiscountAmount} {line.TaxableAmount} {line.CgstAmount} {line.SgstAmount} {line.IgstAmount} {line.CreditAmount}";

    private static string Recorded(WholesaleReturnItem line) =>
        $"{line.ItemName} {line.HsnCode} {line.Unit} {line.BatchNo} {line.BatchExpiry} {line.UnitPrice} {line.TaxRate} {line.DiscountAmount} {line.TaxableAmount} {line.CgstAmount} {line.SgstAmount} {line.IgstAmount} {line.CreditAmount}";

    private static string Recorded(PurchaseReturn note) =>
        $"{note.ReturnNo} {note.ReturnDate} {note.PharmacyStateCode}/{note.SellerStateCode} inter={note.IsInterState} {note.SellerLegalName} | {note.SellerGstin} | {note.SellerAddress} | {note.SellerSnapshotJson} | {note.BuyerSnapshotJson}";

    private static string Recorded(PurchaseReturnItem line) =>
        $"{line.ItemName} {line.HsnCode} {line.Unit} {line.BatchNo} {line.BatchExpiry} {line.UnitPrice} {line.TaxRate} {line.TaxableAmount} {line.CgstAmount} {line.SgstAmount} {line.IgstAmount} {line.LineTotal}";

    private static async Task<string> AllRecordedAsync(PharmaBillDbContext context)
    {
        context.ChangeTracker.Clear();
        var text = new StringBuilder();
        foreach (ReturnNote note in await context.ReturnNotes.AsNoTracking().OrderBy(n => n.ReturnNo).ToListAsync()) text.AppendLine(Recorded(note));
        foreach (SaleReturnItem line in await context.SaleReturnItems.AsNoTracking().OrderBy(l => l.Quantity).ToListAsync()) text.AppendLine(Recorded(line));
        foreach (WholesaleReturnItem line in await context.WholesaleReturnItems.AsNoTracking().OrderBy(l => l.Quantity).ToListAsync()) text.AppendLine(Recorded(line));
        foreach (PurchaseReturn note in await context.PurchaseReturns.AsNoTracking().OrderBy(n => n.ReturnNo).ToListAsync()) text.AppendLine(Recorded(note));
        foreach (PurchaseReturnItem line in await context.PurchaseReturnItems.AsNoTracking().OrderBy(l => l.Quantity).ToListAsync()) text.AppendLine(Recorded(line));
        return text.ToString();
    }

    private static async Task<RetailSaleResult> RetailSaleAsync(Shop shop)
    {
        E2EHealthSeed.Catalog catalog = shop.Catalog;
        Patient patient = catalog.RetailPatients[1];
        return await shop.Database.CreateRetailBillingService(Allow).SaveSaleAsync(
            new SaveRetailSaleInput(patient.Name, patient.Phone!, patient.Address, "Dr. Rehana Qadri", "JKMC-4411", null,
                [new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 4m, 0m, 30m)],
                [new RetailPaymentInput("Cash", 120m)], false),
            catalog.Admin.Id, UserRole.Pharmacist);
    }

    private static async Task<WholesaleInvoiceResult> WholesaleInvoiceAsync(Shop shop, Customer customer) =>
        await shop.Database.CreateWholesaleInvoiceService(Allow).SaveAsync(
            new SaveWholesaleInvoiceInput(customer.Id, [new WholesaleInvoiceLineInput(shop.Catalog.Paracetamol650.Id, shop.Catalog.ParacetamolBatch.Id, 10m, 0m, 28m)],
                0m, "Credit", null, false, 90, false, null, null, null, null, null, "Note test"),
            shop.Catalog.Admin.Id, UserRole.Owner);

    [Fact]
    public async Task ACreditNote_SaysWhatItSaidWhenSaved_AfterEveryMasterChanges()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await OpenAsync(database);
        E2EHealthSeed.Catalog catalog = shop.Catalog;

        RetailSaleResult sale = await RetailSaleAsync(shop);
        RetailBillingService billing = database.CreateRetailBillingService(Allow);
        RetailSaleReturnableLine returnable = (await billing.GetSaleReturnLinesAsync(sale.Sale.Id)).Single();
        await billing.IssueSalesReturnAsync(sale.Sale.Id, [new RetailSaleReturnLineInput(returnable.SaleItemId, 1m)], "Wrong strength", true, catalog.Admin.Id, UserRole.Owner);

        WholesaleInvoiceResult invoice = await WholesaleInvoiceAsync(shop, catalog.BurhanMedicalHall);
        WholesaleInvoiceItem invoiceLine = await database.Context.WholesaleInvoiceItems.AsNoTracking().SingleAsync(item => item.WholesaleInvoiceId == invoice.Invoice.Id);
        await new WholesaleReturnsService(database.CreateUnitOfWork(), Allow).CreateCreditNoteAsync(
            invoice.Invoice.Id, "WCN-1", BusinessClock.Today, [new WholesaleCreditLineInput(invoiceLine.Id, 3m, true)], "Damaged", catalog.Admin.Id, UserRole.Owner);

        // Recorded at save, from the bill / invoice it credits.
        SaleReturnItem retailLine = await database.Context.SaleReturnItems.AsNoTracking().SingleAsync();
        Assert.Equal(("Paracetamol 650mg", catalog.ParacetamolBatch.BatchNo), (retailLine.ItemName, retailLine.BatchNo));
        Assert.Equal(retailLine.CreditAmount, retailLine.TaxableAmount + retailLine.CgstAmount + retailLine.SgstAmount);
        WholesaleReturnItem wholesaleLine = await database.Context.WholesaleReturnItems.AsNoTracking().SingleAsync();
        Assert.Equal((invoiceLine.ItemName, invoiceLine.HsnCode, invoiceLine.TaxRate), (wholesaleLine.ItemName, wholesaleLine.HsnCode, wholesaleLine.TaxRate));
        Assert.Equal(wholesaleLine.CreditAmount, wholesaleLine.TaxableAmount + wholesaleLine.CgstAmount + wholesaleLine.SgstAmount + wholesaleLine.IgstAmount);
        ReturnNote wholesaleNote = await database.Context.ReturnNotes.AsNoTracking().SingleAsync(note => note.ReturnNo == "WCN-1");
        Assert.Equal((catalog.BurhanMedicalHall.Gstin, catalog.BurhanMedicalHall.Name), (wholesaleNote.BuyerGstin, wholesaleNote.BuyerLegalName));

        Guid[] drugs = [catalog.Paracetamol650.Id];
        string before = await EverythingAsync(shop, drugs, catalog.BurhanMedicalHall.Id);
        string recordedBefore = await AllRecordedAsync(database.Context);
        Assert.Contains(catalog.RetailPatients[1].Name, before);
        Assert.Contains(catalog.BurhanMedicalHall.Name, before);

        await ChangeEveryMasterAsync(shop);

        Assert.Equal(recordedBefore, await AllRecordedAsync(database.Context));
        Assert.Equal(before, await EverythingAsync(shop, drugs, catalog.BurhanMedicalHall.Id));
        Assert.DoesNotContain("renamed", before + await EverythingAsync(shop, drugs, catalog.BurhanMedicalHall.Id));
    }

    [Fact]
    public async Task ADebitNote_SaysWhatItSaidWhenSaved_AfterEveryMasterChanges()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await OpenAsync(database);
        E2EHealthSeed.Catalog catalog = shop.Catalog;

        // A debit note to a supplier (purchase return) and a standalone debit note to a customer.
        await database.CreatePurchaseService(Allow).SavePurchaseReturnAsync(catalog.Suppliers[0].Id, "PR-1", BusinessClock.Today,
            [new PurchaseReturnLineInput(catalog.ParacetamolBatch.Id, 2m)], "Damaged strips", catalog.Admin.Id, UserRole.Owner);
        await new WholesaleReturnsService(database.CreateUnitOfWork(), Allow).CreateDebitNoteAsync(
            catalog.BurhanMedicalHall.Id, "WDN-1", BusinessClock.Today, 250m, "Short billed", catalog.Admin.Id, UserRole.Owner);

        PurchaseReturn purchaseReturn = await database.Context.PurchaseReturns.AsNoTracking().SingleAsync();
        Assert.Equal((catalog.Suppliers[0].Name, catalog.Suppliers[0].Gstin), (purchaseReturn.SellerLegalName, purchaseReturn.SellerGstin));
        PurchaseReturnItem purchaseLine = await database.Context.PurchaseReturnItems.AsNoTracking().SingleAsync();
        Assert.Equal("Paracetamol 650mg", purchaseLine.ItemName);
        Assert.True(purchaseLine.CgstAmount + purchaseLine.SgstAmount > 0m, "the debit note records the GST at the purchase rate");
        ReturnNote debit = await database.Context.ReturnNotes.AsNoTracking().SingleAsync(note => note.ReturnNo == "WDN-1");
        Assert.Equal((BusinessClock.Today, catalog.BurhanMedicalHall.Gstin), (debit.ReturnDate!.Value, debit.BuyerGstin));

        var documents = new PurchaseReturnDocumentService(shop.Services.GetRequiredService<IServiceScopeFactory>());
        string pdfBefore = PdfText((await documents.ExportAsync(purchaseReturn.Id)).Path);
        string before = await EverythingAsync(shop, [catalog.Paracetamol650.Id], catalog.BurhanMedicalHall.Id);
        string recordedBefore = await AllRecordedAsync(database.Context);
        Assert.Contains(catalog.Suppliers[0].Name, before);
        Assert.Contains(catalog.Suppliers[0].Name, pdfBefore);

        await ChangeEveryMasterAsync(shop);

        Assert.Equal(recordedBefore, await AllRecordedAsync(database.Context));
        Assert.Equal(before, await EverythingAsync(shop, [catalog.Paracetamol650.Id], catalog.BurhanMedicalHall.Id));
        string pdfAfter = PdfText((await documents.ExportAsync(purchaseReturn.Id)).Path);
        Assert.Equal(pdfBefore, pdfAfter);
        Assert.DoesNotContain("renamed", pdfAfter);
    }

    private static string PdfText(string path)
    {
        using UglyToad.PdfPig.PdfDocument document = UglyToad.PdfPig.PdfDocument.Open(path);
        return string.Join("\n", document.GetPages().Select(page => page.Text));
    }

    [Fact]
    public async Task InterStateAndIntraStateNotes_KeepTheSupplyTypeTheyWereSavedWith()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await OpenAsync(database);
        E2EHealthSeed.Catalog catalog = shop.Catalog;
        // One buyer in Punjab (inter-state from Jammu & Kashmir), one local.
        Customer punjab = await database.Context.Customers.SingleAsync(item => item.Id == catalog.ValleyHospitalPharmacy.Id);
        punjab.State = "Punjab";
        punjab.Gstin = null;
        await database.Context.SaveChangesAsync();

        WholesaleInvoiceResult local = await WholesaleInvoiceAsync(shop, catalog.BurhanMedicalHall);
        WholesaleInvoiceResult outOfState = await new WholesaleInvoiceService(database.CreateUnitOfWork(), new NumberSeriesService(database.CreateUnitOfWork()), Allow, database.CreateBranchService(), new NullLicenseRuntimeGuard())
            .SaveAsync(new SaveWholesaleInvoiceInput(punjab.Id, [new WholesaleInvoiceLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 10m, 0m, 28m)],
                0m, "Credit", null, false, 90, true, null, null, null, null, null, "Inter-state"), catalog.Admin.Id, UserRole.Owner);
        var returns = new WholesaleReturnsService(database.CreateUnitOfWork(), Allow);
        foreach ((WholesaleInvoiceResult invoice, string no) in new[] { (local, "CN-LOCAL"), (outOfState, "CN-PUNJAB") })
        {
            Guid line = await database.Context.WholesaleInvoiceItems.Where(item => item.WholesaleInvoiceId == invoice.Invoice.Id).Select(item => item.Id).SingleAsync();
            await returns.CreateCreditNoteAsync(invoice.Invoice.Id, no, BusinessClock.Today, [new WholesaleCreditLineInput(line, 2m, true)], "Returned", catalog.Admin.Id, UserRole.Owner);
        }

        async Task<(bool? Inter, decimal? Igst, decimal? Cgst)> SupplyAsync(string no)
        {
            database.Context.ChangeTracker.Clear();
            ReturnNote note = await database.Context.ReturnNotes.AsNoTracking().SingleAsync(item => item.ReturnNo == no);
            WholesaleReturnItem line = await database.Context.WholesaleReturnItems.AsNoTracking().SingleAsync(item => item.ReturnNoteId == note.Id);
            return (note.IsInterState, line.IgstAmount, line.CgstAmount);
        }

        (bool? Inter, decimal? Igst, decimal? Cgst) localBefore = await SupplyAsync("CN-LOCAL");
        (bool? Inter, decimal? Igst, decimal? Cgst) punjabBefore = await SupplyAsync("CN-PUNJAB");
        Assert.Equal((false, 0m), (localBefore.Inter, localBefore.Igst!.Value));
        Assert.True(localBefore.Cgst > 0m);
        Assert.Equal((true, 0m), (punjabBefore.Inter, punjabBefore.Cgst!.Value));
        Assert.True(punjabBefore.Igst > 0m);

        // Move both buyers across a border, and the pharmacy too: each note keeps the supply type it applied.
        await ChangeEveryMasterAsync(shop);
        Assert.Equal(localBefore, await SupplyAsync("CN-LOCAL"));
        Assert.Equal(punjabBefore, await SupplyAsync("CN-PUNJAB"));
    }

    [Fact]
    public async Task TheBackfill_FillsEveryRecordedColumn_FromTheInvoiceWhereThereIsOne_AndFlagsTheRest()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        Shop shop = await OpenAsync(database);
        E2EHealthSeed.Catalog catalog = shop.Catalog;
        RetailSaleResult sale = await RetailSaleAsync(shop);
        RetailBillingService billing = database.CreateRetailBillingService(Allow);
        await billing.IssueSalesReturnAsync(sale.Sale.Id, [new RetailSaleReturnLineInput((await billing.GetSaleReturnLinesAsync(sale.Sale.Id)).Single().SaleItemId, 1m)], "Returned", true, catalog.Admin.Id, UserRole.Owner);
        WholesaleInvoiceResult invoice = await WholesaleInvoiceAsync(shop, catalog.BurhanMedicalHall);
        Guid invoiceLineId = await database.Context.WholesaleInvoiceItems.Where(item => item.WholesaleInvoiceId == invoice.Invoice.Id).Select(item => item.Id).SingleAsync();
        var returns = new WholesaleReturnsService(database.CreateUnitOfWork(), Allow);
        await returns.CreateCreditNoteAsync(invoice.Invoice.Id, "WCN-9", BusinessClock.Today, [new WholesaleCreditLineInput(invoiceLineId, 3m, true)], "Damaged", catalog.Admin.Id, UserRole.Owner);
        await returns.CreateDebitNoteAsync(catalog.BurhanMedicalHall.Id, "WDN-9", BusinessClock.Today, 100m, "Short billed", catalog.Admin.Id, UserRole.Owner);
        await database.CreatePurchaseService(Allow).SavePurchaseReturnAsync(catalog.Suppliers[0].Id, "PR-9", BusinessClock.Today,
            [new PurchaseReturnLineInput(catalog.ParacetamolBatch.Id, 2m)], "Damaged", catalog.Admin.Id, UserRole.Owner);
        string asSaved = await AllRecordedAsync(database.Context);

        // As every note looked before this release: nothing recorded.
        foreach (string sql in new[]
        {
            "UPDATE ReturnNotes SET ReturnDate = NULL, PharmacyStateCode = NULL, BuyerStateCode = NULL, BuyerGstin = NULL, BuyerLegalName = NULL, BuyerAddress = NULL, SellerSnapshotJson = NULL, BuyerSnapshotJson = NULL, IsInterState = NULL",
            "UPDATE SaleReturnItems SET ItemName = NULL, HsnCode = NULL, Unit = NULL, BatchNo = NULL, BatchExpiry = NULL, UnitPrice = NULL, TaxRate = NULL, DiscountAmount = NULL, TaxableAmount = NULL, CgstAmount = NULL, SgstAmount = NULL, IgstAmount = NULL",
            "UPDATE WholesaleReturnItems SET ItemName = NULL, HsnCode = NULL, Unit = NULL, BatchNo = NULL, BatchExpiry = NULL, UnitPrice = NULL, TaxRate = NULL, DiscountAmount = NULL, TaxableAmount = NULL, CgstAmount = NULL, SgstAmount = NULL, IgstAmount = NULL",
            "UPDATE PurchaseReturns SET PharmacyStateCode = NULL, SellerStateCode = NULL, SellerGstin = NULL, SellerLegalName = NULL, SellerAddress = NULL, SellerSnapshotJson = NULL, BuyerSnapshotJson = NULL, IsInterState = NULL",
            "UPDATE PurchaseReturnItems SET ItemName = NULL, HsnCode = NULL, Unit = NULL, BatchNo = NULL, BatchExpiry = NULL, TaxRate = NULL, DiscountAmount = NULL, TaxableAmount = NULL, CgstAmount = NULL, SgstAmount = NULL, IgstAmount = NULL",
        })
        {
            await database.Context.Database.ExecuteSqlRawAsync(sql);
        }
        database.Context.ChangeTracker.Clear();

        ReturnNoteBackfillResult result = await ReturnNoteValuesBackfill.RunAsync(database.Context);

        // Credit notes copy their invoices; the debit notes have none, and are flagged.
        Assert.Equal((2, 2), (result.NotesFromInvoice, result.NotesFromMaster));
        database.Context.ChangeTracker.Clear();
        List<ReturnNote> notes = await database.Context.ReturnNotes.AsNoTracking().ToListAsync();
        Assert.All(notes, note => Assert.Equal(note.ReturnNo is "WDN-9", note.ValuesBackfilled));
        Assert.True((await database.Context.PurchaseReturns.AsNoTracking().SingleAsync()).ValuesBackfilled);
        WholesaleReturnItem wholesale = await database.Context.WholesaleReturnItems.AsNoTracking().SingleAsync();
        Assert.False(wholesale.ValuesBackfilled);

        // No recorded column is left null.
        Assert.All(notes, note => Assert.True(
            note.ReturnDate != null && note.PharmacyStateCode != null && note.BuyerStateCode != null && note.BuyerGstin != null && note.BuyerLegalName != null
            && note.BuyerAddress != null && note.SellerSnapshotJson != null && note.BuyerSnapshotJson != null && note.IsInterState != null, note.ReturnNo));
        Assert.All(await database.Context.SaleReturnItems.AsNoTracking().ToListAsync(), line => Assert.True(
            line.ItemName != null && line.HsnCode != null && line.Unit != null && line.BatchNo != null && line.UnitPrice != null && line.TaxRate != null
            && line.DiscountAmount != null && line.TaxableAmount != null && line.CgstAmount != null && line.SgstAmount != null && line.IgstAmount != null));
        Assert.All(await database.Context.WholesaleReturnItems.AsNoTracking().ToListAsync(), line => Assert.True(
            line.ItemName != null && line.HsnCode != null && line.Unit != null && line.BatchNo != null && line.UnitPrice != null && line.TaxRate != null
            && line.DiscountAmount != null && line.TaxableAmount != null && line.CgstAmount != null && line.SgstAmount != null && line.IgstAmount != null));
        PurchaseReturn purchase = await database.Context.PurchaseReturns.AsNoTracking().SingleAsync();
        Assert.True(purchase.PharmacyStateCode != null && purchase.SellerStateCode != null && purchase.SellerGstin != null && purchase.SellerLegalName != null
            && purchase.SellerAddress != null && purchase.SellerSnapshotJson != null && purchase.BuyerSnapshotJson != null && purchase.IsInterState != null);
        Assert.All(await database.Context.PurchaseReturnItems.AsNoTracking().ToListAsync(), line => Assert.True(
            line.ItemName != null && line.HsnCode != null && line.Unit != null && line.BatchNo != null && line.TaxRate != null && line.DiscountAmount != null
            && line.TaxableAmount != null && line.CgstAmount != null && line.SgstAmount != null && line.IgstAmount != null));

        // Nothing changed in the meantime, so the backfill gives back exactly what was recorded at save, and running it again does nothing.
        Assert.Equal(asSaved, await AllRecordedAsync(database.Context));
        Assert.Equal(0, (await ReturnNoteValuesBackfill.RunAsync(database.Context)).Total);
    }

    [Fact]
    public async Task AFreshInstall_CreatesAndSavesACreditNoteAndADebitNote()
    {
        string root = Path.Combine(Path.GetTempPath(), $"PharmaBill.FreshNotes.{Guid.NewGuid():N}");
        try
        {
            var storage = new DatabaseStorageOptions(root);
            Directory.CreateDirectory(storage.DataDirectory);
            await using PharmaBillDbContext context = FolderBackupTests.OpenContext(storage);
            // The real start-up path: an empty database created and migrated to the latest schema.
            await FolderBackupTests.InitializeAsync(context, storage);
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            var catalog = await E2EHealthSeed.SeedAsync(context);
            var uow = new UnitOfWork(context);
            var branches = new BranchService(uow, new BranchSettingsStore(storage), storage);
            WholesaleInvoiceResult invoice = await new WholesaleInvoiceService(uow, new NumberSeriesService(uow), Allow, branches, new NullLicenseRuntimeGuard())
                .SaveAsync(new SaveWholesaleInvoiceInput(catalog.BurhanMedicalHall.Id, [new WholesaleInvoiceLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 5m, 0m, 28m)],
                    0m, "Credit", null, false, 90, false, null, null, null, null, null, "Fresh"), catalog.Admin.Id, UserRole.Owner);
            Guid line = await context.WholesaleInvoiceItems.Where(item => item.WholesaleInvoiceId == invoice.Invoice.Id).Select(item => item.Id).SingleAsync();
            var returns = new WholesaleReturnsService(uow, Allow);
            await returns.CreateCreditNoteAsync(invoice.Invoice.Id, "FRESH-CN", BusinessClock.Today, [new WholesaleCreditLineInput(line, 1m, true)], "Returned", catalog.Admin.Id, UserRole.Owner);
            await returns.CreateDebitNoteAsync(catalog.BurhanMedicalHall.Id, "FRESH-DN", BusinessClock.Today, 50m, "Short billed", catalog.Admin.Id, UserRole.Owner);
            context.ChangeTracker.Clear();
            List<ReturnNote> saved = await context.ReturnNotes.AsNoTracking().Where(note => note.ReturnNo.StartsWith("FRESH")).ToListAsync();
            Assert.Equal(2, saved.Count);
            Assert.All(saved, note => Assert.True(note.SellerSnapshotJson != null && note.BuyerLegalName == catalog.BurhanMedicalHall.Name && note.IsInterState == false && !note.ValuesBackfilled));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
