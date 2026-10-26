using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data;

/// <summary>
/// Applies EF migrations / schema ensure before any application seeding or business queries.
/// Must remain the first data hosted service so other hosts never hit an empty SQLite file.
/// Never deletes or replaces an existing pharmacy database file.
/// </summary>
internal sealed class DatabaseInitializationHostedService(
	IServiceScopeFactory scopeFactory,
	ILogger<DatabaseInitializationHostedService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Database initialization starting (apply migrations; a failed migration stops startup).");
		try
		{
			await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
			await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync(cancellationToken);
			logger.LogInformation("Database initialization completed.");
			if (DbInitializer.LastQuarantinedEmptyDatabase is string moved)
			{
				logger.LogWarning("The database file was empty (no records) and was moved to {Moved}; a fresh database was created.", moved);
			}

			if (DbInitializer.LastArchivedUnreadableDatabase is string archived)
			{
				logger.LogError("The database could not be opened (damaged or a different encryption key). It was archived unchanged at {Archived} and a fresh database was created. Its records can be recovered with the original database.key.", archived);
			}
		}
		catch (Exception ex)
		{
			// Log but do not delete the SQLite file. App may still open read-only / unlock UI.
			logger.LogError(ex, "Database initialization encountered an error; existing database file was preserved.");
			throw;
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}
