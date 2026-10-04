using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using PharmaBill.Core.Security;
using PharmaBill.Core.Sync;

namespace PharmaBill.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPharmaBillData(this IServiceCollection services)
    {
        SQLitePCL.Batteries_V2.Init();

        var appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaBill");
        Directory.CreateDirectory(appDirectory);
        var deviceId = LoadOrCreateDeviceId(Path.Combine(appDirectory, "device.id"));
        var storageOptions = new DatabaseStorageOptions(appDirectory);
        var keyProvider = new DatabaseEncryptionKeyProvider(storageOptions.KeyPath);
        var key = keyProvider.GetOrCreateKey();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = storageOptions.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = Convert.ToHexString(key)
        }.ToString();

        services.AddSingleton(new DatabaseDeviceId(deviceId));
        services.AddSingleton(storageOptions);
        services.AddSingleton(keyProvider);
        services.AddSingleton<HybridLogicalClockState>();
        services.AddSingleton<SaveChangesAuditInterceptor>();
        services.AddDbContext<PharmaBillDbContext>((provider, options) =>
            options.UseSqlite(connectionString)
                .AddInterceptors(provider.GetRequiredService<SaveChangesAuditInterceptor>()));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<NumberSeriesService>();
        services.AddScoped<DbInitializer>();
        services.AddSingleton<ProtectedAccessStateStore>();
        services.AddSingleton<ISubscriptionProvider, StubSubscriptionProvider>();
        services.AddScoped<AccessService>();
        services.AddScoped<IEntitlementService>(provider => provider.GetRequiredService<AccessService>());
        services.AddScoped<AuthorizationService>();
        services.AddScoped<PharmacySetupService>();
        services.AddScoped<BusinessModeService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<CatalogImportService>();
        services.AddScoped<CatalogSearchService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<AddStockService>();
        services.AddScoped<StockVerificationService>();
        services.AddScoped<RetailBillingService>();
        services.AddScoped<WholesaleCustomerService>();
        services.AddScoped<WholesalePricingService>();
        services.AddScoped<WholesaleInvoiceService>();
        services.AddScoped<WholesaleAccountsService>();
        services.AddScoped<WholesaleReturnsService>();
        services.AddScoped<StockInHandService>();
        services.AddScoped<WholesaleGstReportsService>();
        services.AddScoped<ReportsDashboardService>();
        services.AddSingleton<BackupSettingsStore>();
        services.AddScoped<EncryptedBackupService>();
        services.AddSingleton<GeminiApiKeyStore>();
        services.AddSingleton(new HttpClient());
        services.AddSingleton<GeminiPurchaseImportService>();
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddHostedService<AutomaticBackupHostedService>();
        return services;
    }

    private static Guid LoadOrCreateDeviceId(string path)
    {
        if (File.Exists(path))
        {
            var storedId = File.ReadAllText(path);
            if (Guid.TryParse(storedId, out var deviceId))
            {
                return deviceId;
            }

            throw new InvalidDataException("The stored device identifier is invalid.");
        }

        var newId = Guid.NewGuid();
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(newId.ToString("D"));
        }
        catch (IOException) when (File.Exists(path))
        {
            return LoadOrCreateDeviceId(path);
        }

        return newId;
    }
}

internal sealed class DatabaseInitializationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
        await initializer.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
