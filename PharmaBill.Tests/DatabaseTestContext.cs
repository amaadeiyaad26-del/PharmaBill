using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

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
        new(Context, CreateStorageLocations(), CreateBranchService());

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
        var options = new DbContextOptionsBuilder<PharmaBillDbContext>()
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
