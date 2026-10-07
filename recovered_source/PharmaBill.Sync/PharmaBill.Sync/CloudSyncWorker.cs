using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync;

internal sealed class CloudSyncWorker(CloudSyncService service, ILogger<CloudSyncWorker> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using PeriodicTimer timer = new PeriodicTimer(CloudSyncService.Interval);
		do
		{
			try
			{
				await service.PollAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Cloud sync iteration failed.");
			}
		}
		while (await timer.WaitForNextTickAsync(stoppingToken));
	}
}
