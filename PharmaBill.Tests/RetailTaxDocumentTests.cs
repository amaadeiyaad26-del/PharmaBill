using PharmaBill.Core;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class RetailTaxDocumentTests
{
    private sealed class AllowAll : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);

        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private const string Phone = "9111100011";

    private static SaveRetailSaleInput Bill(E2EHealthSeed.Catalog catalog, string patientName, RetailPrintBranding? branding = null) => new(
        patientName,
        Phone,
        "Old Lane",
        null,
        null,
        null,
        [new RetailSaleLineInput(catalog.Paracetamol650.Id, catalog.ParacetamolBatch.Id, 1m, 0m, 30m)],
        [new RetailPaymentInput("Cash", 30m)],
        false,
        PrintBranding: branding);

    [Fact]
    public async Task RetailBill_ReprintsAndReturnsUseDetailsRecordedAtSave()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);
        catalog.Paracetamol650.HsnCode = "30049011";
        await database.Context.SaveChangesAsync();
        var retail = database.CreateRetailBillingService(new AllowAll());
        var branding = new RetailPrintBranding(["20B DL-777"], "FSSAI-1", "Near clock tower", "Goods once sold are not returned.");

        var first = await retail.SaveSaleAsync(Bill(catalog, "Original Person", branding), catalog.Admin.Id, UserRole.Pharmacist);
        Sale saved = await database.Context.Sales.AsNoTracking().SingleAsync(sale => sale.InvoiceNo == first.Sale.InvoiceNo);
        Assert.Equal("01", saved.PharmacyStateCode);

        // Same phone billed again under a new name rewrites the patient master; pharmacy, drug and batch masters change too.
        await retail.SaveSaleAsync(Bill(catalog, "Renamed Person"), catalog.Admin.Id, UserRole.Pharmacist);
        var profile = await database.Context.PharmacyProfiles.SingleAsync();
        profile.Name = "Renamed Pharmacy";
        profile.State = "Karnataka";
        catalog.Paracetamol650.Name = "Renamed Paracetamol";
        catalog.Paracetamol650.HsnCode = "99999999";
        catalog.ParacetamolBatch.BatchNo = "RENAMED-BATCH";
        await database.Context.SaveChangesAsync();

        RetailBillPrintData print = await retail.GetPrintableBillAsync(saved.Id);
        Assert.True(print.DetailsRecorded);
        Assert.Equal("Original Person", print.PatientName);
        Assert.NotEqual("Renamed Pharmacy", print.PharmacyName);
        Assert.Equal("FSSAI-1", print.FssaiNumber);
        Assert.Equal("Near clock tower", print.AddressLine2);
        Assert.Equal("Goods once sold are not returned.", print.TermsText);
        Assert.Contains("20B DL-777", print.LicenceNumbers);
        RetailBillPrintLine line = Assert.Single(print.Items);
        Assert.NotEqual("Renamed Paracetamol", line.DrugName);
        Assert.Equal("30049011", line.HsnCode);
        Assert.NotEqual("RENAMED-BATCH", line.BatchNo);

        var gst = database.CreateGstService();
        var day = saved.SaleAtUtc.ToBusinessDate();
        using var offline = JsonDocument.Parse(await gst.ExportGstr1OfflineJsonAsync(day, day));
        foreach (JsonElement row in offline.RootElement.GetProperty("b2cs").EnumerateArray())
        {
            Assert.Equal("01", row.GetProperty("pos").GetString());
        }
        var tables = await gst.BuildGstr1TablesAsync(day, day);
        Assert.Contains(tables.Table12Hsn, row => row.HsnCode == "30049011");
        Assert.DoesNotContain(saved.InvoiceNo, tables.UnverifiedStateInvoiceNos!);
    }

    [Fact]
    public async Task RetailBill_WithoutRecordedDetails_FallsBackToMasterDataAndIsUnverified()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var catalog = await E2EHealthSeed.SeedAsync(database.Context);
        var retail = database.CreateRetailBillingService(new AllowAll());
        var result = await retail.SaveSaleAsync(Bill(catalog, "Original Person"), catalog.Admin.Id, UserRole.Pharmacist);
        Sale sale = await database.Context.Sales.SingleAsync(item => item.InvoiceNo == result.Sale.InvoiceNo);
        sale.PharmacyStateCode = null;
        sale.SellerSnapshotJson = null;
        sale.BuyerSnapshotJson = null;
        foreach (SaleItem item in await database.Context.SaleItems.Where(item => item.SaleId == sale.Id).ToListAsync())
        {
            item.ItemName = null;
            item.HsnCode = null;
            item.BatchNo = null;
            item.BatchExpiry = null;
        }
        var patient = await database.Context.Patients.SingleAsync(item => item.Phone == Phone);
        patient.Name = "Current Master Name";
        await database.Context.SaveChangesAsync();

        RetailBillPrintData print = await retail.GetPrintableBillAsync(sale.Id);
        Assert.False(print.DetailsRecorded);
        Assert.Equal("Current Master Name", print.PatientName);

        var day = sale.SaleAtUtc.ToBusinessDate();
        var tables = await database.CreateGstService().BuildGstr1TablesAsync(day, day);
        Assert.Contains(sale.InvoiceNo, tables.UnverifiedStateInvoiceNos!);
    }
}
