using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

internal sealed class SyncClockInitializationHostedService(IServiceScopeFactory scopeFactory, HybridLogicalClock clock) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		// Warm the HLC from change logs without blocking MainWindow / host start.
		_ = WarmClockAsync(cancellationToken);
		return Task.CompletedTask;
	}

	private async Task WarmClockAsync(CancellationToken cancellationToken)
	{
		try
		{
			await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
			List<string> stamps = await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().ChangeLogs
				.AsNoTracking()
				.Select(change => change.HlcStamp)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			foreach (string item in stamps)
			{
				try
				{
					clock.Observe(item);
				}
				catch (FormatException)
				{
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch
		{
			// Clock will catch up from subsequent observations.
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
