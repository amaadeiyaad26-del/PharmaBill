using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public static class SyncServiceCollectionExtensions
{
    public static IServiceCollection AddPharmaBillSync(this IServiceCollection services)
    {
        services.AddSingleton<HybridLogicalClock>();
        services.AddSingleton<SyncPayloadSerializer>();
        services.AddSingleton<ProtectedPeerKeyStore>();
        services.AddSingleton<FileTransferQueue>();
        services.AddScoped<SyncDeviceService>();
        services.AddScoped<ChangeApplier>();
        services.AddScoped<SyncPackageService>();
        services.AddSingleton<SyncFolderSettingsStore>();
        services.AddSingleton<SyncFolderExchangeService>();
        services.AddHostedService<SyncFolderPollingService>();
        services.AddSingleton<CloudSyncSettingsStore>();
        services.AddSingleton<ICloudHttpClientProvider, DefaultCloudHttpClientProvider>();
        services.AddSingleton<CloudSyncService>();
        services.AddHostedService<CloudSyncWorker>();
        services.AddHostedService<SyncClockInitializationHostedService>();
        return services;
    }
}

internal sealed class SyncClockInitializationHostedService(
    IServiceScopeFactory scopeFactory,
    HybridLogicalClock clock) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var stamps = await context.ChangeLogs.AsNoTracking()
            .Select(change => change.HlcStamp)
            .ToListAsync(cancellationToken);
        foreach (var stamp in stamps)
        {
            try
            {
                clock.Observe(stamp);
            }
            catch (FormatException)
            {
                // Legacy D2 stamps are intentionally ignored and normalized on export.
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

