// Self-test driver for PharmaRemitMigrationCheck: plays the shop, the upgrade and the reinstall with the
// real StoreDataLocation / DbInitializer code, in a fake profile. Never touches a real data folder.
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

SQLitePCL.Batteries_V2.Init();
string command = args[0];
string root = args[1];
if (!root.Contains("selftest", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Self-test paths only.");

switch (command)
{
    case "seed":
        await SeedAsync(root);
        break;
    case "upgrade":
    {
        DataLocationResult result = StoreDataLocation.Choose(root, args[2]);
        Console.WriteLine($"Choose: {result.Outcome}, {result.FilesCopied} files -> {result.DataRoot}");
        await using (PharmaBillDbContext context = Open(args[2]))
        {
            await Initializer(context, args[2]).InitializeAsync();
        }
        StoreDataLocation.MarkHasDatabase(args[2]);
        break;
    }
    case "reinstall":
    {
        Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        DataLocationResult result = StoreDataLocation.Choose(root, args[2]);
        Console.WriteLine($"Choose after reinstall: {result.Outcome}, {result.FilesCopied} files copied");
        break;
    }
    case "rename":
    {
        await using PharmaBillDbContext context = Open(root);
        await context.Database.ExecuteSqlRawAsync("UPDATE PharmacyProfiles SET Name = {0}", args[2]);
        Console.WriteLine("Shop renamed to " + args[2]);
        break;
    }
    case "tamper":
    {
        await using PharmaBillDbContext context = Open(root);
        Sale last = await context.Sales.OrderByDescending(s => s.InvoiceNo).FirstAsync();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM SaleItems WHERE SaleId = {0}; DELETE FROM Sales WHERE Id = {0};", last.Id);
        Console.WriteLine("Tampered: removed bill " + last.InvoiceNo);
        break;
    }
}
SqliteConnection.ClearAllPools();

static PharmaBillDbContext Open(string root)
{
    var storage = new DatabaseStorageOptions(root);
    Directory.CreateDirectory(storage.DataDirectory);
    string password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(storage.KeyPath).GetOrCreateKey());
    var deviceId = new DatabaseDeviceId(Guid.NewGuid());
    return new PharmaBillDbContext(new DbContextOptionsBuilder<PharmaBillDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = storage.DatabasePath, Password = password, Pooling = false }.ToString())
        .AddInterceptors(new SaveChangesAuditInterceptor(deviceId)).Options, deviceId);
}

static DbInitializer Initializer(PharmaBillDbContext context, string root)
{
    var storage = new DatabaseStorageOptions(root);
    return new DbInitializer(context, new StorageLocationService(context), new BranchService(new UnitOfWork(context), new BranchSettingsStore(storage), storage), storage);
}

static async Task SeedAsync(string root)
{
    await using PharmaBillDbContext context = Open(root);
    await Initializer(context, root).InitializeAsync();
    var storage = new DatabaseStorageOptions(root);
    context.PharmacyProfiles.Add(new PharmacyProfile { Name = "Migration Test Pharmacy", BusinessMode = BusinessMode.Retail, InvoicePrefix = "WIN1", State = "Jammu and Kashmir" });
    await context.SaveChangesAsync();
    var uow = new UnitOfWork(context);
    var allowed = new Allowed();
    var branches = new BranchService(uow, new BranchSettingsStore(storage), storage);
    var purchases = new PurchaseService(uow, allowed, new StorageLocationService(context), branches, new NullLicenseRuntimeGuard());
    var addStock = new AddStockService(uow, purchases, allowed, new NullLicenseRuntimeGuard());
    Guid owner = Guid.NewGuid();
    Supplier supplier = await addStock.AddSupplierAsync("Test Distributor", owner, UserRole.Owner);
    DateOnly today = BusinessClock.Today;
    AddStockResult pcm = await addStock.AddStockAsync(new AddStockInput(null, null, "Paracetamol 650", "Paracetamol", "Acme", "G", "PCM-01",
        today.AddYears(2), 30m, 18m, 100m, 0m, 10m, 5m, supplier.Id, "TD-1", today, "A1", HsnCode: "30049099"), owner, UserRole.Owner);
    AddStockResult alp = await addStock.AddStockAsync(new AddStockInput(null, null, "Alprazolam 0.5", "Alprazolam", "Acme", "H1", "ALP-07",
        today.AddYears(2), 40m, 25m, 20m, 0m, 10m, 12m, supplier.Id, "TD-2", today, "C3", HsnCode: "30049099"), owner, UserRole.Owner);
    var billing = new RetailBillingService(uow, new NumberSeriesService(uow), allowed, new CatalogSearchService(context, branches),
        new StorageLocationService(context), branches, new NullLicenseRuntimeGuard(), storage.PrescriptionsPath);
    foreach (decimal qty in new[] { 2m, 5m, 1m })
    {
        await billing.SaveSaleAsync(new SaveRetailSaleInput("Walk-in", "9876543210", null, null, null, null,
            [new RetailSaleLineInput(pcm.DrugId, pcm.BatchId, qty, 0m, 30m)], [new RetailPaymentInput("Cash", qty * 30m)], false), owner, UserRole.Owner);
    }
    string scan = Path.Combine(Path.GetTempPath(), $"rx-{Guid.NewGuid():N}.jpg");
    await File.WriteAllBytesAsync(scan, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);
    await billing.SaveSaleAsync(new SaveRetailSaleInput("Rahul Kumar", "9876500011", "4 Lal Chowk, Srinagar", "Dr A. Shah", "JKMC-4411", scan,
        [new RetailSaleLineInput(alp.DrugId, alp.BatchId, 10m, 0m, 40m)], [new RetailPaymentInput("Cash", 400m)], false), owner, UserRole.Owner);
    File.Delete(scan);
    Console.WriteLine("Seeded Migration Test Pharmacy in " + root);
}

sealed class Allowed : IEntitlementService
{
    public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);
    public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
}
