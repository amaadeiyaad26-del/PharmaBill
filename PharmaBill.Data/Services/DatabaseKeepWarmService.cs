using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

// Opening the shop's encrypted database costs about 0.8 s, because SQLCipher derives the key on every new connection.
// Pooled connections skip that, but Microsoft.Data.Sqlite drops a connection left idle through two prune cycles
// (140 s each), so after a quiet spell the next bill, search or counter request would pay it again. Touching the pool
// every minute keeps one connection warm.
public sealed class DatabaseKeepWarmService(IServiceScopeFactory scopes, DatabaseStorageOptions storage, ILogger<DatabaseKeepWarmService> logger) : BackgroundService
{
	public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using PeriodicTimer timer = new PeriodicTimer(Interval);
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			try
			{
				await TouchAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception exception)
			{
				logger.LogDebug(exception, "Keeping the database connection warm failed; the next request opens a new one.");
			}
		}
	}

	/// <summary>Uses one pooled connection and returns it. Returns false when it left the database alone.</summary>
	public async Task<bool> TouchAsync(CancellationToken cancellationToken = default)
	{
		if (!DatabaseMaintenance.TryEnter())
		{
			return false;
		}
		try
		{
			// Never create a database: before first-run setup, or while a store is being prepared, there is none to keep warm.
			if (!File.Exists(storage.DatabasePath))
			{
				return false;
			}
			await using AsyncServiceScope scope = scopes.CreateAsyncScope();
			// Reads the schema page, so a connection opened here has its key derived too ("SELECT 1" never touches the file).
			await scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM sqlite_master", cancellationToken);
			return true;
		}
		finally
		{
			DatabaseMaintenance.Exit();
		}
	}
}
