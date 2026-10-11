using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>
/// Switches the active SQLite file to a per-pharmacy folder and ensures a clean empty schema.
/// </summary>
public sealed class StoreDatabaseIsolationService(
	ActiveStoreContext activeStore,
	DatabaseStorageOptions storageOptions,
	IServiceScopeFactory scopeFactory)
{
	public async Task<AppUser> CompleteSetupOnFreshStoreAsync(
		PharmacyProfile profile,
		System.Collections.Generic.IEnumerable<LicenceRecord> licences,
		AppUser owner,
		string ownerSecret,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(profile);
		string slug = ActiveStoreContext.SanitizeStoreName(profile.Name);
		await PrepareFreshStoreAsync(slug, cancellationToken).ConfigureAwait(false);

		using IServiceScope scope = scopeFactory.CreateScope();
		PharmacySetupService setup = scope.ServiceProvider.GetRequiredService<PharmacySetupService>();
		return await setup.CompleteFirstRunOnCurrentStoreAsync(profile, licences, owner, ownerSecret, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task PrepareFreshStoreAsync(string storeSlug, CancellationToken cancellationToken = default)
	{
		SqliteConnection.ClearAllPools();
		activeStore.SetActiveStore(storeSlug);
		DeleteDatabaseFiles(storageOptions.DatabasePath);

		using IServiceScope scope = scopeFactory.CreateScope();
		await scope.ServiceProvider.GetRequiredService<DbInitializer>()
			.InitializeAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	public static void DeleteDatabaseFiles(string databasePath)
	{
		TryDelete(databasePath);
		TryDelete(databasePath + "-wal");
		TryDelete(databasePath + "-shm");
		TryDelete(databasePath + "-journal");
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (IOException)
		{
			// Retry once after clearing pools — common when a previous scope just closed.
			try
			{
				SqliteConnection.ClearAllPools();
				if (File.Exists(path))
				{
					File.Delete(path);
				}
			}
			catch
			{
			}
		}
	}
}
