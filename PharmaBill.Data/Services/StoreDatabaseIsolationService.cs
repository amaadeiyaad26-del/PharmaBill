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
		await DatabaseMaintenance.RunExclusiveAsync(storageOptions.DatabasePath, "a new pharmacy is being set up", () =>
		{
			activeStore.SetActiveStore(storeSlug);
			SetAsideExistingDatabase(storageOptions.DatabasePath);
			// Deliberately a new, empty database for this store.
			DatabaseCreation.Forget(storageOptions.DatabasePath);
			return Task.CompletedTask;
		}, cancellationToken).ConfigureAwait(false);

		using IServiceScope scope = scopeFactory.CreateScope();
		await scope.ServiceProvider.GetRequiredService<DbInitializer>()
			.InitializeAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// A database already at the new store's path is never deleted: it is renamed beside itself to
	/// "pharmabill.db.set-aside-&lt;time&gt;" (with its -wal / -shm files), so its records can still be recovered.
	/// If it cannot be renamed, setup stops and nothing is changed.
	/// </summary>
	public static string? SetAsideExistingDatabase(string databasePath)
	{
		if (!File.Exists(databasePath))
		{
			foreach (string suffix in new[] { "-wal", "-shm", "-journal" })
			{
				if (File.Exists(databasePath + suffix))
				{
					DatabaseFileHealth.Quarantine(databasePath + suffix, "orphan");
				}
			}
			return null;
		}
		try
		{
			return DatabaseFileHealth.Quarantine(databasePath, "set-aside");
		}
		catch (IOException)
		{
			// Retry once after closing this database's idle pooled connections (a previous scope may just have closed).
			DatabaseMaintenance.ClearPools(databasePath);
			try
			{
				return DatabaseFileHealth.Quarantine(databasePath, "set-aside");
			}
			catch (IOException ex)
			{
				throw new InvalidOperationException("A database already exists at " + databasePath + " and could not be set aside, so the new pharmacy was not set up. Close other programs using it and try again. Nothing was changed. " + ex.Message, ex);
			}
		}
	}
}
