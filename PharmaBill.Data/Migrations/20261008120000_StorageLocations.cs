using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

#nullable disable

namespace PharmaBill.Data.Migrations;

/// <summary>
/// Creates core tables that exist in the model snapshot but were never emitted by
/// historical migrations (StorageLocations, Branches, stock-transfer tables).
/// </summary>
[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261008120000_StorageLocations")]
public partial class StorageLocationsMigration : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.Sql("""
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
			""");
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.Sql("""
			DROP TABLE IF EXISTS "StockTransferItems";
			DROP TABLE IF EXISTS "StockTransfers";
			DROP TABLE IF EXISTS "StockLocationBalances";
			DROP TABLE IF EXISTS "StorageLocations";
			DROP TABLE IF EXISTS "Branches";
			""");
	}
}