using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync;

internal sealed class SyncFolderPollingService(SyncFolderExchangeService exchangeService, ILogger<SyncFolderPollingService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using PeriodicTimer timer = new PeriodicTimer(SyncFolderExchangeService.PollInterval);
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try
			{
				await exchangeService.PollAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Folder sync polling iteration failed.");
			}
		}
	}
}
