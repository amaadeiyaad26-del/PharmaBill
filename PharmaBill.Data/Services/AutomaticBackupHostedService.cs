using PharmaBill.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Data.Services;

public sealed class AutomaticBackupHostedService(BackupSettingsStore settingsStore, FolderBackupService folderBackup, ILogger<AutomaticBackupHostedService> logger) : BackgroundService
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
		if (!settings.AutomaticEnabled || string.IsNullOrWhiteSpace(settings.Password))
		{
			return;
		}
		DateOnly dateOnly = BusinessClock.Today;
		if (settings.LastBackupAtUtc.HasValue && settings.LastBackupAtUtc.Value.ToBusinessDate() >= dateOnly)
		{
			return;
		}
		// Same file as "Back up now": shop name and time, never overwriting an earlier one.
		await folderBackup.BackUpNowAsync(cancellationToken);
	}
}
