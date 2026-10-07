using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Data.Services;

public sealed class AutomaticBackupHostedService(BackupSettingsStore settingsStore, IServiceScopeFactory scopeFactory, ILogger<AutomaticBackupHostedService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromMinutes(15L));
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
		BackupSettings settings = settingsStore.Load();
		if (!settings.AutomaticEnabled || string.IsNullOrWhiteSpace(settings.PrimaryFolder) || string.IsNullOrWhiteSpace(settings.Password))
		{
			return;
		}
		DateOnly dateOnly = DateOnly.FromDateTime(DateTime.Now);
		if (settings.LastBackupAtUtc.HasValue && DateOnly.FromDateTime(settings.LastBackupAtUtc.Value.ToLocalTime()) >= dateOnly)
		{
			return;
		}
		Directory.CreateDirectory(settings.PrimaryFolder);
		string destination = Path.Combine(settings.PrimaryFolder, $"PharmaBill-{dateOnly:yyyyMMdd}.pbbak");
		using IServiceScope scope = scopeFactory.CreateScope();
		await scope.ServiceProvider.GetRequiredService<EncryptedBackupService>().CreateBackupAsync(destination, settings.Password, cancellationToken);
		if (!string.IsNullOrWhiteSpace(settings.SecondaryFolder))
		{
			Directory.CreateDirectory(settings.SecondaryFolder);
			await using FileStream source = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true);
			await using FileStream copy = new FileStream(Path.Combine(settings.SecondaryFolder, Path.GetFileName(destination)), FileMode.Create, FileAccess.Write, FileShare.None, 131072, useAsync: true);
			await source.CopyToAsync(copy, cancellationToken);
			await copy.FlushAsync(cancellationToken);
		}
		settingsStore.Save(settings with
		{
			LastBackupAtUtc = DateTime.UtcNow
		});
	}
}
