using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.Data.Persistence;

public sealed class DbInitializer(
	PharmaBillDbContext context,
	StorageLocationService storageLocations,
	BranchService branchService,
	DatabaseStorageOptions storageOptions,
	DatabaseEncryptionKeyProvider? keyProvider = null)
{
	/// <summary>Where the last empty database file was moved this process, for the startup log. Null when none was.</summary>
	public static string? LastQuarantinedEmptyDatabase { get; private set; }

	/// <summary>Where an unreadable database was archived this process, so startup can tell the user. Null when none was.</summary>
	public static string? LastArchivedUnreadableDatabase { get; private set; }

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

		// 0) CHECK THE FILE before EF opens it (SQLite Error 26 used to crash startup here).
		CheckDatabaseFile();

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

	/// <summary>
	/// A file that provably holds no records (0 bytes or only zeros) is moved aside. A non-empty file that will not
	/// open (damaged, or encrypted with a different key) is archived next to itself as ".key-mismatch-&lt;time&gt;" or
	/// ".unreadable-&lt;time&gt;", never deleted. Either way the migrations then create a fresh database so the app starts.
	/// Only when the file cannot even be archived (held open elsewhere) does startup stop, with
	/// <see cref="DatabaseUnreadableException"/>, so the recovery screen can explain it.
	/// </summary>
	private void CheckDatabaseFile()
	{
		string? connectionString = context.Database.GetConnectionString();
		if (string.IsNullOrWhiteSpace(connectionString))
		{
			return;
		}

		DatabaseFileCheck check = DatabaseFileHealth.Inspect(connectionString);
		switch (check.State)
		{
			case DatabaseFileState.Empty:
			{
				string path = new SqliteConnectionStringBuilder(connectionString).DataSource;
				try
				{
					LastQuarantinedEmptyDatabase = DatabaseFileHealth.Quarantine(path, "empty");
				}
				catch (IOException) when (new FileInfo(path).Length == 0)
				{
					// A 0-byte file still open elsewhere (SQLite creates one when a connection first opens a new
					// database). SQLite initialises a 0-byte file as a new empty database, so it can stay.
				}
				catch (IOException ex)
				{
					throw new DatabaseUnreadableException(path, check.Detail + " It could not be moved aside: " + ex.Message, keyProvider?.CreatedNewKey == true, isUnencryptedSqlite: false);
				}

				break;
			}
			case DatabaseFileState.Unreadable:
			{
				string path = new SqliteConnectionStringBuilder(connectionString).DataSource;
				bool newKey = keyProvider?.CreatedNewKey == true;
				try
				{
					// The key file was missing at startup, so the archived file almost certainly needs the original key.
					LastArchivedUnreadableDatabase = DatabaseFileHealth.Quarantine(path, newKey ? "key-mismatch" : "unreadable");
				}
				catch (IOException ex)
				{
					throw new DatabaseUnreadableException(path, check.Detail + " It could not be archived: " + ex.Message, newKey, check.IsUnencryptedSqlite);
				}

				break;
			}
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
