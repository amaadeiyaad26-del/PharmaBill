using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Services;

namespace PharmaBill.Data.Persistence;

public sealed class DbInitializer(
	PharmaBillDbContext context,
	StorageLocationService storageLocations,
	BranchService branchService,
	DatabaseStorageOptions storageOptions)
{
	public async Task InitializeAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		// NEVER delete, truncate, or recreate storageOptions.DatabasePath here.
		// Preloaded Drug Bank catalogues (116–119MB) must survive schema upgrades in-place.
		EnsureDatabaseDirectory();

		// Patch critical columns before migrate/seed so setup never hits "no such column".
		await TryAddAuditLogsBranchIdAsync(cancellationToken);

		// 1) CREATE / MIGRATE SCHEMA — tolerate existing preloaded DBs that already have tables.
		try
		{
			await context.Database.MigrateAsync(cancellationToken);
		}
		catch (Exception)
		{
			// Keep the existing SQLite file. Safety-net DDL below brings schema up to date.
		}

		// 2) Safety net for tables present in the model but missing from older migration history.
		try
		{
			await EnsureStorageSchemaAsync(cancellationToken);
		}
		catch (SqliteException)
		{
			// CREATE IF NOT EXISTS should be safe; ignore rare SQLite lock/busy races.
		}

		// 3) Additive column patches for installed DBs that skipped EF column migrations.
		await EnsureSocialAuthColumnsAsync(cancellationToken);
		await EnsureUserManualEmailColumnsAsync(cancellationToken);
		await EnsureStockMovementLocationColumnAsync(cancellationToken);
		await EnsureBranchIdColumnsAsync(cancellationToken);

		// 4) SEED ONLY AFTER TABLES EXIST — skip cleanly when defaults already present.
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
	}

	private async Task TryAddAuditLogsBranchIdAsync(CancellationToken cancellationToken)
	{
		await TryAddNullableTextColumnAsync("AuditLogs", "BranchId", "IX_AuditLogs_BranchId", cancellationToken);
	}

	private void EnsureDatabaseDirectory()
	{
		string? directory = Path.GetDirectoryName(storageOptions.DatabasePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		Directory.CreateDirectory(storageOptions.DataDirectory);
		Directory.CreateDirectory(storageOptions.BackupsPath);
	}

	private async Task EnsureStorageSchemaAsync(CancellationToken cancellationToken)
	{
		// Idempotent safety net for tables that were in the EF model/snapshot but missing from older migrations.
		await context.Database.ExecuteSqlRawAsync("""
			CREATE TABLE IF NOT EXISTS "StorageLocations" (
				"Id" TEXT NOT NULL CONSTRAINT "PK_StorageLocations" PRIMARY KEY,
				"Name" TEXT NOT NULL,
				"Code" TEXT NOT NULL,
				"IsDefaultRetailLocation" INTEGER NOT NULL,
				"IsActive" INTEGER NOT NULL,
				"CreatedAtUtc" TEXT NOT NULL,
				"UpdatedAtUtc" TEXT NOT NULL,
				"DeviceId" TEXT NOT NULL,
				"IsDeleted" INTEGER NOT NULL,
				"HlcStamp" TEXT NOT NULL,
				"SyncState" INTEGER NOT NULL
			);
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_StorageLocations_Code" ON "StorageLocations" ("Code");

			CREATE TABLE IF NOT EXISTS "StockLocationBalances" (
				"Id" TEXT NOT NULL CONSTRAINT "PK_StockLocationBalances" PRIMARY KEY,
				"LocationId" TEXT NOT NULL,
				"BatchId" TEXT NOT NULL,
				"DrugId" TEXT NOT NULL,
				"Quantity" decimal(18,2) NOT NULL,
				"CreatedAtUtc" TEXT NOT NULL,
				"UpdatedAtUtc" TEXT NOT NULL,
				"DeviceId" TEXT NOT NULL,
				"IsDeleted" INTEGER NOT NULL,
				"HlcStamp" TEXT NOT NULL,
				"SyncState" INTEGER NOT NULL
			);
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_StockLocationBalances_LocationId_BatchId"
				ON "StockLocationBalances" ("LocationId", "BatchId");

			CREATE TABLE IF NOT EXISTS "Branches" (
				"Id" TEXT NOT NULL CONSTRAINT "PK_Branches" PRIMARY KEY,
				"Code" TEXT NOT NULL,
				"BranchName" TEXT NOT NULL,
				"Address" TEXT NULL,
				"ContactPhone" TEXT NULL,
				"Gstin" TEXT NULL,
				"DrugLicenseNo" TEXT NULL,
				"IsHeadOffice" INTEGER NOT NULL,
				"InvoicePrefix" TEXT NOT NULL,
				"IsActive" INTEGER NOT NULL,
				"CreatedAtUtc" TEXT NOT NULL,
				"UpdatedAtUtc" TEXT NOT NULL,
				"DeviceId" TEXT NOT NULL,
				"IsDeleted" INTEGER NOT NULL,
				"HlcStamp" TEXT NOT NULL,
				"SyncState" INTEGER NOT NULL
			);
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_Branches_Code" ON "Branches" ("Code");

			CREATE TABLE IF NOT EXISTS "StockTransfers" (
				"Id" TEXT NOT NULL CONSTRAINT "PK_StockTransfers" PRIMARY KEY,
				"TransferNumber" TEXT NOT NULL,
				"SourceLocationId" TEXT NOT NULL,
				"DestinationLocationId" TEXT NOT NULL,
				"TransferDate" TEXT NOT NULL,
				"Notes" TEXT NULL,
				"Status" TEXT NOT NULL,
				"CreatedAtUtc" TEXT NOT NULL,
				"UpdatedAtUtc" TEXT NOT NULL,
				"DeviceId" TEXT NOT NULL,
				"IsDeleted" INTEGER NOT NULL,
				"HlcStamp" TEXT NOT NULL,
				"SyncState" INTEGER NOT NULL
			);
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_StockTransfers_TransferNumber" ON "StockTransfers" ("TransferNumber");
			CREATE INDEX IF NOT EXISTS "IX_StockTransfers_TransferDate" ON "StockTransfers" ("TransferDate");

			CREATE TABLE IF NOT EXISTS "StockTransferItems" (
				"Id" TEXT NOT NULL CONSTRAINT "PK_StockTransferItems" PRIMARY KEY,
				"StockTransferId" TEXT NOT NULL,
				"BatchId" TEXT NOT NULL,
				"DrugId" TEXT NOT NULL,
				"QuantityTransferred" decimal(18,2) NOT NULL,
				"CreatedAtUtc" TEXT NOT NULL,
				"UpdatedAtUtc" TEXT NOT NULL,
				"DeviceId" TEXT NOT NULL,
				"IsDeleted" INTEGER NOT NULL,
				"HlcStamp" TEXT NOT NULL,
				"SyncState" INTEGER NOT NULL
			);
			CREATE INDEX IF NOT EXISTS "IX_StockTransferItems_StockTransferId" ON "StockTransferItems" ("StockTransferId");
			""", cancellationToken);
	}

	private async Task EnsureStockMovementLocationColumnAsync(CancellationToken cancellationToken)
	{
		if (!await TableExistsAsync("StockMovements", cancellationToken))
		{
			return;
		}

		await TryAddNullableTextColumnAsync("StockMovements", "LocationId", indexName: null, cancellationToken);

		await EnsureIndexAsync(
			"IX_StockMovements_LocationId_BatchId",
			"""CREATE INDEX IF NOT EXISTS "IX_StockMovements_LocationId_BatchId" ON "StockMovements" ("LocationId", "BatchId");""",
			cancellationToken);
	}

	private async Task EnsureBranchIdColumnsAsync(CancellationToken cancellationToken)
	{
		// Model snapshot has nullable BranchId on these tables; historical CreateTable migrations omitted them.
		await TryAddNullableTextColumnAsync("AuditLogs", "BranchId", "IX_AuditLogs_BranchId", cancellationToken);
		await TryAddNullableTextColumnAsync("Batches", "BranchId", "IX_Batches_BranchId", cancellationToken);
		await TryAddNullableTextColumnAsync("PurchaseInvoices", "BranchId", "IX_PurchaseInvoices_BranchId", cancellationToken);
		await TryAddNullableTextColumnAsync("Sales", "BranchId", "IX_Sales_BranchId", cancellationToken);
		await TryAddNullableTextColumnAsync("WholesaleInvoices", "BranchId", indexName: null, cancellationToken);
	}

	private async Task TryAddNullableTextColumnAsync(
		string tableName,
		string columnName,
		string? indexName,
		CancellationToken cancellationToken)
	{
		if (!await TableExistsAsync(tableName, cancellationToken))
		{
			return;
		}

		HashSet<string> columns = await GetTableColumnsAsync(tableName, cancellationToken);
		if (!columns.Contains(columnName))
		{
			try
			{
				await context.Database.ExecuteSqlRawAsync(
					$"""ALTER TABLE "{tableName}" ADD COLUMN "{columnName}" TEXT NULL;""",
					cancellationToken);
			}
			catch (SqliteException ex) when (
				ex.SqliteErrorCode == 1
				&& ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
			{
				// Race / concurrent startup — column appeared between PRAGMA and ALTER.
			}
			catch (Exception ex) when (
				ex.InnerException is SqliteException inner
				&& inner.SqliteErrorCode == 1
				&& inner.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
			{
				// EF-wrapped duplicate column.
			}
		}

		if (!string.IsNullOrWhiteSpace(indexName))
		{
			await EnsureIndexAsync(
				indexName,
				$"""CREATE INDEX IF NOT EXISTS "{indexName}" ON "{tableName}" ("{columnName}");""",
				cancellationToken);
		}
	}

	private async Task EnsureUserManualEmailColumnsAsync(CancellationToken cancellationToken)
	{
		if (!await TableExistsAsync("PharmacyProfiles", cancellationToken))
		{
			return;
		}

		HashSet<string> columns = await GetTableColumnsAsync("PharmacyProfiles", cancellationToken);
		if (!columns.Contains("UserManualEmailSent"))
		{
			await context.Database.ExecuteSqlRawAsync(
				"ALTER TABLE PharmacyProfiles ADD COLUMN UserManualEmailSent INTEGER NOT NULL DEFAULT 0;",
				cancellationToken);
		}

		if (!columns.Contains("UserManualSentAtUtc"))
		{
			await context.Database.ExecuteSqlRawAsync(
				"ALTER TABLE PharmacyProfiles ADD COLUMN UserManualSentAtUtc TEXT NULL;",
				cancellationToken);
		}
	}

	private async Task EnsureSocialAuthColumnsAsync(CancellationToken cancellationToken)
	{
		if (!await TableExistsAsync("AppUsers", cancellationToken))
		{
			return;
		}

		HashSet<string> columns = await GetTableColumnsAsync("AppUsers", cancellationToken);
		if (!columns.Contains("AuthProvider"))
		{
			await context.Database.ExecuteSqlRawAsync(
				"ALTER TABLE AppUsers ADD COLUMN AuthProvider TEXT NULL;",
				cancellationToken);
		}

		if (!columns.Contains("ProviderSubjectId"))
		{
			await context.Database.ExecuteSqlRawAsync(
				"ALTER TABLE AppUsers ADD COLUMN ProviderSubjectId TEXT NULL;",
				cancellationToken);
		}

		columns = await GetTableColumnsAsync("AppUsers", cancellationToken);
		if (columns.Contains("AuthProvider") && columns.Contains("ProviderSubjectId"))
		{
			await EnsureIndexAsync(
				"IX_AppUsers_AuthProvider_ProviderSubjectId",
				"CREATE INDEX IF NOT EXISTS IX_AppUsers_AuthProvider_ProviderSubjectId ON AppUsers (AuthProvider, ProviderSubjectId);",
				cancellationToken);
		}

		if (columns.Contains("Email"))
		{
			await EnsureIndexAsync(
				"IX_AppUsers_Email",
				"CREATE INDEX IF NOT EXISTS IX_AppUsers_Email ON AppUsers (Email);",
				cancellationToken);
		}
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

	private async Task<HashSet<string>> GetTableColumnsAsync(string tableName, CancellationToken cancellationToken)
	{
		HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		DbConnection connection = context.Database.GetDbConnection();
		if (connection.State != ConnectionState.Open)
		{
			await connection.OpenAsync(cancellationToken);
		}

		await using DbCommand command = connection.CreateCommand();
		command.CommandText = "PRAGMA table_info(\"" + tableName + "\");";
		await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			if (!reader.IsDBNull(1))
			{
				columns.Add(reader.GetString(1));
			}
		}

		return columns;
	}

	private async Task EnsureIndexAsync(string indexName, string createSql, CancellationToken cancellationToken)
	{
		DbConnection connection = context.Database.GetDbConnection();
		if (connection.State != ConnectionState.Open)
		{
			await connection.OpenAsync(cancellationToken);
		}

		await using (DbCommand check = connection.CreateCommand())
		{
			check.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $name LIMIT 1;";
			DbParameter dbParameter = check.CreateParameter();
			dbParameter.ParameterName = "$name";
			dbParameter.Value = indexName;
			check.Parameters.Add(dbParameter);
			if (await check.ExecuteScalarAsync(cancellationToken) != null)
			{
				return;
			}
		}

		await context.Database.ExecuteSqlRawAsync(createSql, cancellationToken);
	}
}