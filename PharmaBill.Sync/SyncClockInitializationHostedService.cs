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
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		foreach (string item in await (from change in scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().ChangeLogs.AsNoTracking()
			select change.HlcStamp).ToListAsync(cancellationToken))
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

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
