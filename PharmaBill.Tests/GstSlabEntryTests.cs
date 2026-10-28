using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Gst;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// The GST rate is 0, 5, 12, 18 or 28 wherever it is entered. A CGST half (2.5, 6, 9) is answered with "Enter the total
// GST rate, not the CGST half — for 5% GST, enter 5." The spreadsheet import also reads an HSN column.
public sealed class GstSlabEntryTests
{
    [Theory]
    [InlineData(0, null)]
    [InlineData(5, null)]
    [InlineData(12, null)]
    [InlineData(18, null)]
    [InlineData(28, null)]
    [InlineData(2.5, GstRates.HalfRateMessage)]
    [InlineData(6, GstRates.HalfRateMessage)]
    [InlineData(9, GstRates.HalfRateMessage)]
    [InlineData(14, GstRates.HalfRateMessage)]
    [InlineData(7, GstRates.SlabMessage)]
    [InlineData(40, GstRates.SlabMessage)]
    public void Problem_AcceptsTheSlabs_AndExplainsAHalf(double rate, string? expected) =>
        Assert.Equal(expected, GstRates.Problem((decimal)rate));

    [Fact]
    public void HalfRateMessage_IsTheAgreedWording() =>
        Assert.Equal("Enter the total GST rate, not the CGST half — for 5% GST, enter 5.", GstRates.HalfRateMessage);

    [Fact]
    public void AddStockForm_RefusesAHalfRate()
    {
        var form = new AddStockViewModel(null, null, new BackupPageEndToEndTests.AlwaysYes(), null) { GstText = "2.5" };
        form.Validate();
        Assert.Equal(GstRates.HalfRateMessage, form.GstError);
        form.GstText = "5";
        form.Validate();
        Assert.Equal(string.Empty, form.GstError);
    }

    [Fact]
    public void RecordedHalfRate_IsNotAKnownRate_SoBillingAsksForIt()
    {
        Assert.Null(ProductTax.Known("3004", 2.5m, false));
        Assert.Equal("GST rate", ProductTax.DescribeMissing("3004", 2.5m, false));
        Assert.NotNull(ProductTax.Known("3004", 5m, false));
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    [Fact]
    public async Task PurchaseService_RefusesAHalfRateOnALine()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var uow = new UnitOfWork(database.Context);
        var allowed = new Allowed();
        var stock = new AddStockService(uow, database.CreatePurchaseService(allowed, uow), allowed, new NullLicenseRuntimeGuard());
        Supplier supplier = await stock.AddSupplierAsync("Sup", Guid.NewGuid(), UserRole.Owner);
        AddStockInput Input(decimal gst) => new(null, null, "Dolo 650 Tablet", null, null, "H", "B1", BusinessClock.Today.AddYears(1), 30m, 20m, 1m, 0m, 1m, gst,
            supplier.Id, "INV-" + gst, BusinessClock.Today, null, HsnCode: "3004");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => stock.AddStockAsync(Input(6m), Guid.NewGuid(), UserRole.Owner));
        Assert.Contains("not the CGST half", refused.Message);
        await stock.AddStockAsync(Input(12m), Guid.NewGuid(), UserRole.Owner);
    }

    private sealed class SpreadsheetPicker(string path) : IFilePickerService
    {
        public string? PickFolder(string title) => null;
        public string? PickBackupFile() => null;
        public string? PickLicenceDocument() => null;
        public string? PickCsvFile() => null;
        public string? PickPurchaseDocument() => null;
        public IReadOnlyList<string> PickInvoiceDocuments() => [];
        public string? PickPurchaseSpreadsheet() => path;
        public string? PickCustomerExcel() => null;
        public string? PickPrescriptionDocument() => null;
        public string? PickExportDestination(string extension, string suggestedName) => null;
        public string? PickSyncPackage() => null;
    }

    [Fact]
    public void SpreadsheetImport_ReadsTheHsnColumn_AndSkipsAHalfRateRow()
    {
        Ui.Run(async () =>
        {
            await using var database = await DatabaseTestContext.CreateAsync();
            var catalog = await E2EHealthSeed.SeedAsync(database.Context);
            string csv = Path.Combine(TestAppData.Root, "purchase-" + Guid.NewGuid().ToString("N") + ".csv");
            string expiry = BusinessClock.Today.AddYears(2).ToString("MM/yyyy");
            await File.WriteAllTextAsync(csv,
                "Medicine,HSN Code,Batch,Expiry,Qty,Rate,GST %\n"
                + $"Zentrix 250 Tablet,3004 20 19,ZT01,{expiry},10,40,12\n"
                + $"Halfrate 500 Tablet,30049099,HR01,{expiry},5,20,2.5\n");
            ServiceProvider services = new ServiceCollection().AddSingleton(database.Context).BuildServiceProvider();
            var session = new CurrentSession();
            session.SignIn(catalog.Admin);
            var page = new PurchasePageViewModel(services.GetRequiredService<IServiceScopeFactory>(), session, new SpreadsheetPicker(csv), new BackupPageEndToEndTests.AlwaysYes(),
                new PurchaseSpreadsheetReader(), null!, null!, null!, null!, null!, null!);

            await page.LoadSpreadsheetCommand.ExecuteAsync(null);

            Assert.Equal("HSN Code", page.SpreadsheetMappings.Single(mapping => mapping.Target == "HSN").SourceHeader);
            PurchaseLineDraft line = Assert.Single(page.Lines);
            Assert.Equal(("30042019", 12m), (line.HsnCode, line.GstRate));
            Assert.Contains("Halfrate 500 Tablet", page.ErrorMessage);
            Assert.Contains(GstRates.HalfRateMessage, page.ErrorMessage);
        });
    }
}
