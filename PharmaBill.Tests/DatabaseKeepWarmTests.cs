using System.Collections;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

// A pooled connection to the encrypted database stays open and keyed, so requests skip SQLCipher's key derivation
// (about 0.8 s). Microsoft.Data.Sqlite drops connections idle through two prune cycles (140 s each); these tests run
// its real prune step instead of waiting minutes. Runs alone: other tests call SqliteConnection.ClearAllPools(), which
// empties every pool in the process, this one's included.
[Collection(RunsAlone.Name)]
public sealed class DatabaseKeepWarmTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"PharmaBill.KeepWarm.{Guid.NewGuid():N}");
    private readonly DatabaseStorageOptions _storage;
    private readonly ServiceProvider _services;
    private readonly string _connectionString;

    public DatabaseKeepWarmTests()
    {
        SQLitePCL.Batteries_V2.Init();
        _storage = new DatabaseStorageOptions(_root);
        Directory.CreateDirectory(_storage.DataDirectory);
        // The production connection string's shape (ServiceCollectionExtensions.AddPharmaBillData): pooled by default.
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _storage.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToHexString(new DatabaseEncryptionKeyProvider(_storage.KeyPath).GetOrCreateKey()),
        }.ToString();
        var services = new ServiceCollection();
        services.AddScoped(_ => new PharmaBillDbContext(new DbContextOptionsBuilder<PharmaBillDbContext>().UseSqlite(_connectionString).Options, new DatabaseDeviceId(Guid.NewGuid())));
        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutIt_AQuietSpellDropsTheConnection_WithItOneStaysOpen()
    {
        DatabaseKeepWarmService keepWarm = await ShopDatabaseAsync();
        object pool = Pool();

        // A quiet spell with nothing touching the pool: two prune cycles and the connection is gone.
        Prune(pool);
        Prune(pool);
        Assert.Equal(0, Pooled(pool));
        AssertNotCleared(pool);

        // The same spell with the keep-warm touch between cycles (it runs every 60 s, a cycle is 140 s).
        Assert.True(await keepWarm.TouchAsync());
        for (int cycle = 0; cycle < 5; cycle++)
        {
            Prune(pool);
            Assert.True(await keepWarm.TouchAsync());
        }
        Prune(pool);
        AssertNotCleared(pool);
        Assert.Equal(1, Pooled(pool));

        // The next real request reuses it: no new connection, so no key derivation.
        int opened = Opened(pool);
        await using (AsyncServiceScope scope = _services.CreateAsyncScope())
        {
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().Sales.CountAsync());
        }
        Assert.Equal(opened, Opened(pool));
        Assert.True(DatabaseKeepWarmService.Interval < TimeSpan.FromSeconds(140), "the touch must come more often than a prune cycle");
    }

    [Fact]
    public async Task LeavesTheDatabaseAlone_WhileItIsReplaced_OrWhenThereIsNone()
    {
        var keepWarm = new DatabaseKeepWarmService(_services.GetRequiredService<IServiceScopeFactory>(), _storage, NullLogger<DatabaseKeepWarmService>.Instance);

        // Before first-run setup there is no database, and keeping it warm must not create one.
        Assert.False(await keepWarm.TouchAsync());
        Assert.False(File.Exists(_storage.DatabasePath));

        await ShopDatabaseAsync();
        await DatabaseMaintenance.RunExclusiveAsync(_storage.DatabasePath, "a backup is being restored", async () =>
        {
            Assert.False(await keepWarm.TouchAsync());
        });
        Assert.True(await keepWarm.TouchAsync());
    }

    private async Task<DatabaseKeepWarmService> ShopDatabaseAsync()
    {
        await using (AsyncServiceScope scope = _services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().Database.EnsureCreatedAsync();
        }
        return new DatabaseKeepWarmService(_services.GetRequiredService<IServiceScopeFactory>(), _storage, NullLogger<DatabaseKeepWarmService>.Instance);
    }

    private object Pool()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        object inner = Field(connection, "SqliteConnectionInternal");
        return Field(inner, "SqliteConnectionPool");
    }

    // Pruning never disables a pool; ClearPool/ClearAllPools does. A cleared pool would make either count meaningless.
    private static void AssertNotCleared(object pool) =>
        Assert.NotEqual("Disabled", pool.GetType().GetField("_state", Private)?.GetValue(pool)?.ToString());

    private static void Prune(object pool)
    {
        MethodInfo prune = pool.GetType().GetMethod("PruneCallback", Private)
            ?? throw new InvalidOperationException("Microsoft.Data.Sqlite no longer has SqliteConnectionPool.PruneCallback; recheck how it drops idle connections.");
        prune.Invoke(pool, prune.GetParameters().Length == 0 ? [] : [null]);
    }

    /// <summary>Idle connections the pool still holds open.</summary>
    private static int Pooled(object pool) => Count(pool, "_warmPool") + Count(pool, "_coldPool");

    /// <summary>Every connection the pool has open, idle or in use.</summary>
    private static int Opened(object pool) => Count(pool, "_connections");

    private static int Count(object pool, string field) =>
        ((ICollection)(pool.GetType().GetField(field, Private)?.GetValue(pool)
            ?? throw new InvalidOperationException($"Microsoft.Data.Sqlite's pool has no {field}; recheck how it drops idle connections."))).Count;

    private static object Field(object target, string typeName) =>
        target.GetType().GetFields(Private).Select(field => field.GetValue(target)).FirstOrDefault(value => value?.GetType().Name == typeName)
            ?? throw new InvalidOperationException($"{target.GetType().Name} no longer holds a {typeName}.");
}
