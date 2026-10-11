using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Data.Services.Dunning;

namespace PharmaBill.Tests;

internal sealed class DatabaseTestContext : IAsyncDisposable
{
    private readonly string _directory;

    private DatabaseTestContext(
        string directory,
        SqliteConnection connection,
        PharmaBillDbContext context)
    {
        _directory = directory;
        Connection = connection;
        Context = context;
        Storage = new DatabaseStorageOptions(directory);
        BranchSettings = new BranchSettingsStore(Storage);
    }

    public SqliteConnection Connection { get; }

    public PharmaBillDbContext Context { get; }

    public DatabaseStorageOptions Storage { get; }

    public BranchSettingsStore BranchSettings { get; }

    public UnitOfWork CreateUnitOfWork() => new(Context);

    public StorageLocationService CreateStorageLocations() => new(Context);

    public BranchService CreateBranchService(IUnitOfWork? unitOfWork = null) =>
        new(unitOfWork ?? CreateUnitOfWork(), BranchSettings, Storage);

    public CatalogSearchService CreateCatalogSearch() =>
        new(Context, CreateBranchService());

    public PurchaseService CreatePurchaseService(IEntitlementService entitlements, IUnitOfWork? unitOfWork = null)
    {
        var uow = unitOfWork ?? CreateUnitOfWork();
        return new PurchaseService(uow, entitlements, CreateStorageLocations(), CreateBranchService(uow), new NullLicenseRuntimeGuard());
    }

    public DbInitializer CreateDbInitializer() =>
        new(Context, CreateStorageLocations(), CreateBranchService(), Storage);

    public RetailBillingService CreateRetailBillingService(IEntitlementService entitlements, string? prescriptionStorageDirectory = null)
    {
        var uow = CreateUnitOfWork();
        return new RetailBillingService(
            uow,
            new NumberSeriesService(uow),
            entitlements,
            CreateCatalogSearch(),
            CreateStorageLocations(),
            CreateBranchService(uow),
            new NullLicenseRuntimeGuard(),
            prescriptionStorageDirectory);
    }

    public WholesaleInvoiceService CreateWholesaleInvoiceService(IEntitlementService entitlements)
    {
        var uow = CreateUnitOfWork();
        return new WholesaleInvoiceService(
            uow,
            new NumberSeriesService(uow),
            entitlements,
            CreateBranchService(uow),
            new NullLicenseRuntimeGuard());
    }

    public GstService CreateGstService() =>
        new(Context, new GstReturnExportService(Context));

    public WholesaleDashboardService CreateWholesaleDashboardService()
    {
        var uow = CreateUnitOfWork();
        return new WholesaleDashboardService(Context, new WholesaleAccountsService(uow), new DunningService(uow));
    }

    public WholesaleAccountsService CreateWholesaleAccountsService() =>
        new(CreateUnitOfWork());

    public LedgerRepairService CreateLedgerRepairService() =>
        new(Context);

    public static async Task<DatabaseTestContext> CreateAsync(bool createSchema = true)
    {
        SQLitePCL.Batteries_V2.Init();
        var directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "test.db"),
            Pooling = false,
            Password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        }.ToString());
        await connection.OpenAsync();
        var deviceId = new DatabaseDeviceId(Guid.NewGuid());
        // Mirror production (ServiceCollectionExtensions): columns added by DbInitializer's
        // runtime schema guards are not in a migration, so ignore the pending-model warning.
        var options = new DbContextOptionsBuilder<PharmaBillDbContext>()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .UseSqlite(connection)
            .AddInterceptors(new SaveChangesAuditInterceptor(deviceId))
            .Options;
        var context = new PharmaBillDbContext(options, deviceId);
        if (createSchema)
        {
            await context.Database.EnsureCreatedAsync();
        }

        return new DatabaseTestContext(directory, connection, context);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Connection.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }
}
