using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PharmaBill.Sync;

public sealed class LocalSyncHostedService(ZeroConfigSyncCoordinator zeroConfig, ILogger<LocalSyncHostedService> logger) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		// Do not block host/UI startup on firewall, Kestrel bind, or cloud fallback.
		_ = InitializeInBackgroundAsync(cancellationToken);
		return Task.CompletedTask;
	}

	private async Task InitializeInBackgroundAsync(CancellationToken cancellationToken)
	{
		try
		{
			await zeroConfig.InitializeAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "Zero-config LAN / cloud sync failed to start.");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return zeroConfig.ShutdownAsync();
	}
}
