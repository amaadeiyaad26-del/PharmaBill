using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.Data.Persistence;

public sealed class DbInitializer(
	PharmaBillDbContext context,
	StorageLocationService storageLocations,
	BranchService branchService,
	DatabaseStorageOptions storageOptions)
{
	// Databases already initialised in this process. Startup paths (hosted service, login window, store switch)
	// may all ask; migrations and data tidy-ups must run once per database file per launch.
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> InitializedDatabases = new(StringComparer.OrdinalIgnoreCase);

	public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		string databaseKey = Path.GetFullPath(storageOptions.DatabasePath);
		if (InitializedDatabases.ContainsKey(databaseKey))
		{
			return;
		}
		await InitializeOnceAsync(cancellationToken);
		InitializedDatabases.TryAdd(databaseKey, 0);
	}

	private async Task InitializeOnceAsync(CancellationToken cancellationToken)
	{
		// NEVER delete, truncate, or recreate storageOptions.DatabasePath here.
		// Preloaded Drug Bank catalogues (116–119MB) must survive schema upgrades in-place.
		EnsureDatabaseDirectory();

		// 1) CREATE / MIGRATE SCHEMA — must run before any PharmacyProfiles / AppUsers query.
		// Migrations are the only thing that changes the schema. A failed migration throws
		// DatabaseMigrationException and startup stops; nothing patches columns or tables behind them.
		await EnsureCoreSchemaAsync(cancellationToken);

		// Store buyer-type synonyms (Wholesaler, Stockist…) as Distributor, with an audit entry per remap.
		try
		{
			await BuyerTypeRemapping.NormalizeExistingAsync(context, cancellationToken);
		}
		catch (Exception)
		{
			// Retried on the next launch.
		}

		// Fill in default licence-type rules for any standard buyer type that has none (never overrides a configured rule).
		try
		{
			foreach (PharmacyProfile profile in await context.PharmacyProfiles.ToListAsync(cancellationToken))
			{
				(string json, bool changed) = PharmaBill.Core.Wholesale.BuyerLicenceRules.AddMissingDefaults(profile.WholesaleBuyerLicenceRulesJson);
				if (changed)
				{
					profile.WholesaleBuyerLicenceRulesJson = json;
				}
			}
			await context.SaveChangesAsync(cancellationToken);
		}
		catch (Exception)
		{
			// Retried on the next launch; billing explains the missing rule meanwhile.
		}

		// Split legacy free-text customer "State / Area" into a canonical State and an Area.
		try
		{
			await PharmaBill.Data.Services.CustomerStateArea.NormalizeExistingAsync(context, cancellationToken);
		}
		catch (Exception)
		{
			// Data tidy-up must never block startup; it retries on the next launch.
		}

		// 4) SEED ONLY AFTER TABLES EXIST — skip cleanly when defaults already present.
		// Never inject demo/sample inventory, stock batches, sales, or statutory register rows.
		// New pharmacies start empty until purchases are inwarded or items are imported.
		try
		{
			await storageLocations.EnsureDefaultRetailLocationAsync(cancellationToken);
		}
		catch (Exception)
		{
			// Do not wipe DB or abort startup if seed races an existing catalogue.
		}

		try
		{
			await branchService.EnsureCurrentBranchAsync(cancellationToken);
		}
		catch (Exception)
		{
			// Branch seed is best-effort when a preloaded DB already has branch rows.
		}

		// 5) Repair orphaned purchase bills missing SupplierLedger PurchaseBill credits.
		try
		{
			await new LedgerRepairService(context).SyncOrphanedInvoicesToLedgerAsync(cancellationToken);
		}
		catch (Exception)
		{
			// Ledger backfill must never block app startup.
		}
	}

	/// <summary>
	/// Applies migrations once. A failure stops startup and names the migration; there is no retry,
	/// EnsureCreated fallback or column patching that would hide a broken migration history.
	/// </summary>
	private async Task EnsureCoreSchemaAsync(CancellationToken cancellationToken)
	{
		List<string> pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
		try
		{
			await context.Database.MigrateAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			HashSet<string> applied;
			try
			{
				applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
			}
			catch (Exception)
			{
				applied = new HashSet<string>(StringComparer.Ordinal);
			}
			string failed = pending.FirstOrDefault(id => !applied.Contains(id)) ?? "(unknown)";
			throw new DatabaseMigrationException(failed, storageOptions.DatabasePath, ex);
		}

		if (!await TableExistsAsync("PharmacyProfiles", cancellationToken))
		{
			throw new InvalidOperationException(
				"Migrations completed but the PharmacyProfiles table is missing; the database at "
				+ storageOptions.DatabasePath + " is not a Pharma Remit database. Restore a backup or contact support.");
		}
	}

	private void EnsureDatabaseDirectory()
	{
		Directory.CreateDirectory(storageOptions.DataDirectory);
		Directory.CreateDirectory(storageOptions.BackupsPath);
		// Active-store path may differ from data\pharmabill.db; seed/copy before SQLCipher opens it.
		AppDataPaths.EnsureSeedDatabaseIfMissing(storageOptions.DatabasePath);
	}

	private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
	{
		DbConnection connection = context.Database.GetDbConnection();
		if (connection.State != ConnectionState.Open)
		{
			await connection.OpenAsync(cancellationToken);
		}

		await using DbCommand command = connection.CreateCommand();
		command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name LIMIT 1;";
		DbParameter parameter = command.CreateParameter();
		parameter.ParameterName = "$name";
		parameter.Value = tableName;
		command.Parameters.Add(parameter);
		return await command.ExecuteScalarAsync(cancellationToken) != null;
	}
}
