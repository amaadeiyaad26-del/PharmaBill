using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data;

internal sealed class DatabaseInitializationHostedService(IServiceScopeFactory scopeFactory) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync(cancellationToken);
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
