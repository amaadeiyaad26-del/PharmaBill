using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync;

public sealed class LocalSyncHostedService(ZeroConfigSyncCoordinator zeroConfig, ILogger<LocalSyncHostedService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			await zeroConfig.InitializeAsync(cancellationToken);
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			logger.LogWarning(ex, "Zero-config LAN / cloud sync failed to start.");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return zeroConfig.ShutdownAsync();
	}
}
