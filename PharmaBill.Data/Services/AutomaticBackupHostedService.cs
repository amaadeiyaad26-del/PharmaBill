using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Data.Services;

public sealed class AutomaticBackupHostedService(
    BackupSettingsStore settingsStore,
    IServiceScopeFactory scopeFactory,
    ILogger<AutomaticBackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await TryBackupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Automatic encrypted backup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TryBackupAsync(CancellationToken cancellationToken)
    {
        var settings = settingsStore.Load();
        if (!settings.AutomaticEnabled ||
            string.IsNullOrWhiteSpace(settings.PrimaryFolder) ||
            string.IsNullOrWhiteSpace(settings.Password))
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (settings.LastBackupAtUtc.HasValue &&
            DateOnly.FromDateTime(settings.LastBackupAtUtc.Value.ToLocalTime()) >= today)
        {
            return;
        }

        Directory.CreateDirectory(settings.PrimaryFolder);
        var destination = Path.Combine(settings.PrimaryFolder, $"PharmaBill-{today:yyyyMMdd}.pbbak");
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>()
            .CreateBackupAsync(destination, settings.Password, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.SecondaryFolder))
        {
            Directory.CreateDirectory(settings.SecondaryFolder);
            await using var source = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
            await using var copy = new FileStream(
                Path.Combine(settings.SecondaryFolder, Path.GetFileName(destination)),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                131072,
                true);
            await source.CopyToAsync(copy, cancellationToken);
            await copy.FlushAsync(cancellationToken);
        }

        settingsStore.Save(settings with { LastBackupAtUtc = DateTime.UtcNow });
    }
}
