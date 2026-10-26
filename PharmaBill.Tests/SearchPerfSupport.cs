using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.Tests;

internal sealed class CommandTimingInterceptor : DbCommandInterceptor
{
    public ConcurrentQueue<(string Sql, double Ms)> Commands { get; } = new();

    private void Add(DbCommand command, CommandExecutedEventData eventData) =>
        Commands.Enqueue((string.Join(' ', command.CommandText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), eventData.Duration.TotalMilliseconds));

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Add(command, eventData);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Add(command, eventData);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Add(command, eventData);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Add(command, eventData);
        return ValueTask.FromResult(result);
    }
}

/// <summary>A production-shaped service stack (scoped DbContext per search, like the view models create) over an arbitrary SQLCipher file.</summary>
internal sealed class SearchStack : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private SearchStack(ServiceProvider provider, string connectionString, CommandTimingInterceptor timing)
    {
        _provider = provider;
        ConnectionString = connectionString;
        Timing = timing;
    }

    public string ConnectionString { get; }

    public CommandTimingInterceptor Timing { get; }

    public IServiceScopeFactory Scopes => _provider.GetRequiredService<IServiceScopeFactory>();

    public static SearchStack Open(string databasePath, string passwordHex, string storageRoot, bool pooling = true)
    {
        SQLitePCL.Batteries_V2.Init();
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = passwordHex,
            Pooling = pooling
        }.ToString();
        CommandTimingInterceptor timing = new CommandTimingInterceptor();
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton(new DatabaseDeviceId(Guid.NewGuid()));
        services.AddSingleton(new DatabaseStorageOptions(storageRoot));
        services.AddSingleton<BranchSettingsStore>();
        services.AddSingleton(timing);
        services.AddDbContext<PharmaBillDbContext>((IServiceProvider provider, DbContextOptionsBuilder options) =>
            options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
                .UseSqlite(connectionString)
                .AddInterceptors(provider.GetRequiredService<CommandTimingInterceptor>(), SqliteWalInterceptor.Instance));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<NumberSeriesService>();
        services.AddScoped<BranchService>();
        services.AddScoped<StorageLocationService>();
        services.AddScoped<CatalogSearchService>();
        services.AddScoped<IEntitlementService, AllowAllEntitlements>();
        services.AddScoped<ILicenseRuntimeGuard, NullLicenseRuntimeGuard>();
        services.AddScoped<RetailBillingService>();
        return new SearchStack(services.BuildServiceProvider(), connectionString, timing);
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private sealed class AllowAllEntitlements : IEntitlementService
    {
        public Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(EntitlementStatus.TRIAL);

        public Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
