using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <summary>
    /// Columns and indexes that were in the model snapshot but never created by any migration; installed
    /// databases only had them because startup patched the schema. Hand-written: the snapshot already
    /// contains them, so the scaffolder produces an empty migration.
    /// </summary>
    public partial class BackfillUnmigratedSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // AppUsers — social sign-in
            migrationBuilder.AddColumn<string>(name: "AuthProvider", table: "AppUsers", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "ProviderSubjectId", table: "AppUsers", type: "TEXT", nullable: true);

            // AuditLogs — compliance trail and branch
            migrationBuilder.AddColumn<string>(name: "AuthorizedBy", table: "AuditLogs", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "BranchId", table: "AuditLogs", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "EntityType", table: "AuditLogs", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "NewSnapshotJson", table: "AuditLogs", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "OldSnapshotJson", table: "AuditLogs", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Reason", table: "AuditLogs", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "RecordHash", table: "AuditLogs", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<DateTime>(name: "Timestamp", table: "AuditLogs", type: "TEXT", nullable: true);

            // Batches — bans and branch
            migrationBuilder.AddColumn<string>(name: "BanReason", table: "Batches", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "BranchId", table: "Batches", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsBanned", table: "Batches", type: "INTEGER", nullable: false, defaultValue: false);

            // CatalogMedicines
            migrationBuilder.AddColumn<bool>(name: "IsCustom", table: "CatalogMedicines", type: "INTEGER", nullable: false, defaultValue: false);

            // Drugs — bans, reorder ceiling, cached stock
            migrationBuilder.AddColumn<bool>(name: "IsBanned", table: "Drugs", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<decimal>(name: "MaxStockLevel", table: "Drugs", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "StockQuantity", table: "Drugs", type: "decimal(18,2)", nullable: false, defaultValue: 0m);

            // PharmacyProfiles — welcome manual e-mail
            migrationBuilder.AddColumn<bool>(name: "UserManualEmailSent", table: "PharmacyProfiles", type: "INTEGER", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<DateTime>(name: "UserManualSentAtUtc", table: "PharmacyProfiles", type: "TEXT", nullable: true);

            // PurchaseInvoices — attachments, branch, inward location
            migrationBuilder.AddColumn<string>(name: "AttachedInvoicePath", table: "PurchaseInvoices", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "BranchId", table: "PurchaseInvoices", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "OriginalFileName", table: "PurchaseInvoices", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "StorageLocationId", table: "PurchaseInvoices", type: "TEXT", nullable: true);

            // Sales — lock, biller, branch, MRD, prescription file. Existing bills are locked, as on save.
            migrationBuilder.AddColumn<Guid>(name: "BilledByUserId", table: "Sales", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "BranchId", table: "Sales", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsLocked", table: "Sales", type: "INTEGER", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<string>(name: "MrdNumber", table: "Sales", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "PrescriptionFilePath", table: "Sales", type: "TEXT", nullable: true);

            // StockMovements / WholesaleInvoices
            migrationBuilder.AddColumn<Guid>(name: "LocationId", table: "StockMovements", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "BranchId", table: "WholesaleInvoices", type: "TEXT", nullable: true);

            // Superseded by the descending idx_*_date indexes below; earlier migrations never dropped them.
            migrationBuilder.DropIndex(name: "IX_PurchaseInvoices_InvoiceDate", table: "PurchaseInvoices");
            migrationBuilder.DropIndex(name: "IX_Sales_SaleAtUtc", table: "Sales");
            migrationBuilder.DropIndex(name: "IX_StockMovements_MovementAtUtc", table: "StockMovements");
            migrationBuilder.DropIndex(name: "IX_WholesaleInvoices_InvoiceAtUtc", table: "WholesaleInvoices");

            // Indexes
            migrationBuilder.CreateIndex(name: "IX_AppUsers_AuthProvider_ProviderSubjectId", table: "AppUsers", columns: new[] { "AuthProvider", "ProviderSubjectId" });
            migrationBuilder.CreateIndex(name: "IX_AppUsers_Email", table: "AppUsers", column: "Email");
            migrationBuilder.CreateIndex(name: "IX_AuditLogs_BranchId", table: "AuditLogs", column: "BranchId");
            migrationBuilder.CreateIndex(name: "IX_Batches_BranchId", table: "Batches", column: "BranchId");
            migrationBuilder.CreateIndex(name: "IX_PurchaseInvoices_BranchId", table: "PurchaseInvoices", column: "BranchId");
            migrationBuilder.CreateIndex(name: "idx_purchases_date", table: "PurchaseInvoices", column: "InvoiceDate", descending: new bool[0]);
            migrationBuilder.CreateIndex(name: "IX_Sales_BranchId", table: "Sales", column: "BranchId");
            migrationBuilder.CreateIndex(name: "idx_sales_date", table: "Sales", column: "SaleAtUtc", descending: new bool[0]);
            migrationBuilder.CreateIndex(name: "IX_StockMovements_LocationId_BatchId", table: "StockMovements", columns: new[] { "LocationId", "BatchId" });
            migrationBuilder.CreateIndex(name: "idx_stock_movements_date", table: "StockMovements", column: "MovementAtUtc", descending: new bool[0]);
            migrationBuilder.CreateIndex(name: "idx_wholesale_invoices_date", table: "WholesaleInvoices", column: "InvoiceAtUtc", descending: new bool[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach ((string name, string table) in new[]
            {
                ("IX_AppUsers_AuthProvider_ProviderSubjectId", "AppUsers"), ("IX_AppUsers_Email", "AppUsers"),
                ("IX_AuditLogs_BranchId", "AuditLogs"), ("IX_Batches_BranchId", "Batches"),
                ("IX_PurchaseInvoices_BranchId", "PurchaseInvoices"), ("idx_purchases_date", "PurchaseInvoices"),
                ("IX_Sales_BranchId", "Sales"), ("idx_sales_date", "Sales"),
                ("IX_StockMovements_LocationId_BatchId", "StockMovements"), ("idx_stock_movements_date", "StockMovements"),
                ("idx_wholesale_invoices_date", "WholesaleInvoices")
            })
            {
                migrationBuilder.DropIndex(name: name, table: table);
            }

            foreach ((string name, string table) in new[]
            {
                ("AuthProvider", "AppUsers"), ("ProviderSubjectId", "AppUsers"),
                ("AuthorizedBy", "AuditLogs"), ("BranchId", "AuditLogs"), ("EntityType", "AuditLogs"), ("NewSnapshotJson", "AuditLogs"),
                ("OldSnapshotJson", "AuditLogs"), ("Reason", "AuditLogs"), ("RecordHash", "AuditLogs"), ("Timestamp", "AuditLogs"),
                ("BanReason", "Batches"), ("BranchId", "Batches"), ("IsBanned", "Batches"),
                ("IsCustom", "CatalogMedicines"),
                ("IsBanned", "Drugs"), ("MaxStockLevel", "Drugs"), ("StockQuantity", "Drugs"),
                ("UserManualEmailSent", "PharmacyProfiles"), ("UserManualSentAtUtc", "PharmacyProfiles"),
                ("AttachedInvoicePath", "PurchaseInvoices"), ("BranchId", "PurchaseInvoices"), ("OriginalFileName", "PurchaseInvoices"), ("StorageLocationId", "PurchaseInvoices"),
                ("BilledByUserId", "Sales"), ("BranchId", "Sales"), ("IsLocked", "Sales"), ("MrdNumber", "Sales"), ("PrescriptionFilePath", "Sales"),
                ("LocationId", "StockMovements"), ("BranchId", "WholesaleInvoices")
            })
            {
                migrationBuilder.DropColumn(name: name, table: table);
            }

            migrationBuilder.CreateIndex(name: "IX_PurchaseInvoices_InvoiceDate", table: "PurchaseInvoices", column: "InvoiceDate");
            migrationBuilder.CreateIndex(name: "IX_Sales_SaleAtUtc", table: "Sales", column: "SaleAtUtc");
            migrationBuilder.CreateIndex(name: "IX_StockMovements_MovementAtUtc", table: "StockMovements", column: "MovementAtUtc");
            migrationBuilder.CreateIndex(name: "IX_WholesaleInvoices_InvoiceAtUtc", table: "WholesaleInvoices", column: "InvoiceAtUtc");
        }
    }
}
