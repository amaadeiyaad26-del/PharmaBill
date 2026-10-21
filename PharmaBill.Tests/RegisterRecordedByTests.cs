using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class RegisterRecordedByTests
{
    private const string MigrationBeforeRecordedBy = "20261021095025_PharmacyLogo";

    [Fact]
    public async Task NewEntries_RecordWhoMadeThem()
    {
        string root = NewRoot();
        try
        {
            await using var context = await OpenAsync(root);
            var shop = await SeedAsync(context, root);

            ScheduleRegisterEntry sale = await context.ScheduleRegisterEntries.AsNoTracking().SingleAsync(e => e.SaleId == shop.SaleId);
            Assert.Equal(shop.Pharmacist.Id, sale.RecordedByUserId);
            Assert.Equal("Asha (pharmacist)", sale.RecordedByName);
            Assert.Equal(RegisterRecordedBy.AtEntry, sale.RecordedBySource);
            ScheduleRegisterEntry writeOff = await context.ScheduleRegisterEntries.AsNoTracking().SingleAsync(e => e.RegisterType == "ExpiryDump");
            Assert.Equal(shop.Owner.Id, writeOff.RecordedByUserId);
            Assert.Equal("Asha (pharmacist)", RegisterRecordedBy.Describe(sale));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task OlderEntries_FilledOnlyWhereTracedExactly_RestNotRecorded()
    {
        string root = NewRoot();
        try
        {
            Shop shop;
            await using (var context = await OpenAsync(root))
            {
                shop = await SeedAsync(context, root);
                // An entry that arrived from a phone: no bill on this PC, no audit row in its save.
                context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry { RegisterType = "H1", BatchNo = "PHONE-1", Quantity = 1m, PatientName = "Phone patient" });
                await context.SaveChangesAsync();

                // Go back to the schema before this change and forward again, so every entry is "older".
                IMigrator migrator = context.GetService<IMigrator>();
                await migrator.MigrateAsync(MigrationBeforeRecordedBy);
                await migrator.MigrateAsync();
            }
            SqliteConnection.ClearAllPools();

            await using var after = await OpenAsync(root, initialize: false);
            List<ScheduleRegisterEntry> entries = await after.ScheduleRegisterEntries.AsNoTracking().ToListAsync();

            ScheduleRegisterEntry sale = entries.Single(e => e.SaleId == shop.SaleId);
            Assert.Equal(RegisterRecordedBy.FromBill, sale.RecordedBySource);
            Assert.Equal(shop.Pharmacist.Id, sale.RecordedByUserId);
            Assert.Equal("Asha (pharmacist) (from bill)", RegisterRecordedBy.Describe(sale));

            ScheduleRegisterEntry writeOff = entries.Single(e => e.RegisterType == "ExpiryDump");
            Assert.Equal(RegisterRecordedBy.FromAuditLog, writeOff.RecordedBySource);
            Assert.Equal(shop.Owner.Id, writeOff.RecordedByUserId);
            Assert.Equal("Ravi (owner)", writeOff.RecordedByName);

            ScheduleRegisterEntry phone = entries.Single(e => e.BatchNo == "PHONE-1");
            Assert.Null(phone.RecordedByUserId);
            Assert.Null(phone.RecordedBySource);
            Assert.Equal("Not recorded", RegisterRecordedBy.Describe(phone));

            // The legal record itself is untouched.
            Assert.Equal(shop.SaleEntry, Describe(sale));
        }
        finally
        {
            Cleanup(root);
        }
    }

    private sealed record Shop(AppUser Owner, AppUser Pharmacist, Guid SaleId, string SaleEntry);

    private static string Describe(ScheduleRegisterEntry e) =>
        $"{e.RegisterType}|{e.BatchNo}|{e.Quantity:0.##}|{e.PatientName}|{e.PrescriberName}|{e.PrescriberRegistrationNumber}|{e.EntryAtUtc:yyyy-MM-dd HH:mm:ss.fffffff}|{e.HlcStamp}|{e.Notes}";

    private static async Task<Shop> SeedAsync(PharmaBillDbContext context, string root)
    {
        var storage = new DatabaseStorageOptions(root);
        var owner = new AppUser { DisplayName = "Ravi (owner)", UserName = "ravi", Role = UserRole.Owner };
        var pharmacist = new AppUser { DisplayName = "Asha (pharmacist)", UserName = "asha", Role = UserRole.Pharmacist };
        context.AppUsers.AddRange(owner, pharmacist);
        context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Register Pharmacy", BusinessMode = BusinessMode.Retail, InvoicePrefix = "WIN1" });
        var alprazolam = new Drug { Name = "Alprazolam 0.5", Schedule = "H1", GstRate = 12m, HsnCode = "3004" };
        var stock = new Batch { DrugId = alprazolam.Id, BatchNo = "ALP-1", ExpiryDate = BusinessClock.Today.AddMonths(6), Quantity = 10m, Mrp = 40m };
        var expired = new Batch { DrugId = alprazolam.Id, BatchNo = "ALP-OLD", ExpiryDate = BusinessClock.Today.AddDays(-3), Quantity = 4m, Mrp = 40m };
        context.AddRange(alprazolam, stock, expired);
        await context.SaveChangesAsync();
        var uow = new UnitOfWork(context);
        var locations = new StorageLocationService(context);
        StorageLocation retail = await locations.GetDefaultRetailLocationAsync();
        foreach ((Batch batch, decimal quantity) in new[] { (stock, 10m), (expired, 4m) })
        {
            context.StockMovements.Add(new StockMovement { BatchId = batch.Id, DrugId = alprazolam.Id, LocationId = retail.Id, QuantityChange = quantity, MovementType = "PurchaseReceipt" });
            await StorageLocationService.ApplyBalanceDeltaAsync(context, retail.Id, batch.Id, alprazolam.Id, quantity);
        }
        await context.SaveChangesAsync();

        var branches = new BranchService(uow, new BranchSettingsStore(storage), storage);
        var billing = new RetailBillingService(uow, new NumberSeriesService(uow), new Allowed(), new CatalogSearchService(context, branches), locations, branches, new NullLicenseRuntimeGuard());
        RetailSaleResult sale = await billing.SaveSaleAsync(new SaveRetailSaleInput("Rahul Kumar", "9876500011", "4 Lal Chowk, Srinagar", "Dr A. Shah", "JKMC-4411", null,
            [new RetailSaleLineInput(alprazolam.Id, stock.Id, 2m, 0m, 40m)], [new RetailPaymentInput("Cash", 80m)], false, PrescriptionAdminOverride: true),
            pharmacist.Id, UserRole.Owner);
        await new InventoryService(context, uow, new Allowed()).WriteOffExpiredBatchAsync(expired.Id, 4m, "Expired, sent for destruction", owner.Id, UserRole.Owner);

        Guid saleId = (await context.Sales.AsNoTracking().SingleAsync()).Id;
        ScheduleRegisterEntry entry = await context.ScheduleRegisterEntries.AsNoTracking().SingleAsync(e => e.SaleId == saleId);
        return new Shop(owner, pharmacist, saleId, Describe(entry));
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), $"PharmaBill.Register.{Guid.NewGuid():N}");

    private static async Task<PharmaBillDbContext> OpenAsync(string root, bool initialize = true)
    {
        SQLitePCL.Batteries_V2.Init();
        var storage = new DatabaseStorageOptions(root);
        Directory.CreateDirectory(Path.GetDirectoryName(storage.DatabasePath)!);
        Directory.CreateDirectory(storage.DataDirectory);
        string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(storage.KeyPath).GetOrCreateKey());
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Password = password, Pooling = false }.ToString())
            .AddInterceptors(new SaveChangesAuditInterceptor(deviceId))
            .Options;
        var context = new PharmaBillDbContext(options, deviceId);
        if (initialize)
        {
            var branches = new BranchService(new UnitOfWork(context), new BranchSettingsStore(storage), storage);
            await new DbInitializer(context, new StorageLocationService(context), branches, storage).InitializeAsync();
        }
        return context;
    }

    private static void Cleanup(string root)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Allowed : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
