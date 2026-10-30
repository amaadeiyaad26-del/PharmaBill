using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpiryReturnClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Additive only. An existing pharmacy starts with the same default expiry-return window as a new one
            // (90 days before expiry, 30 after); every supplier follows it until given its own.
            migrationBuilder.AddColumn<int>(
                name: "ExpiryReturnDaysAfter",
                table: "Suppliers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpiryReturnDaysBefore",
                table: "Suppliers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpiryReturnPolicyNote",
                table: "Suppliers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExpiryReturnDaysAfter",
                table: "PharmacyProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "ExpiryReturnDaysBefore",
                table: "PharmacyProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.CreateTable(
                name: "ExpiryClaimCreditLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiryClaimCreditId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiryClaimLineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShortfallValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShortfallReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SyncState = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpiryClaimCreditLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpiryClaimCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiryClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreditNoteNo = table.Column<string>(type: "TEXT", nullable: false),
                    CreditNoteDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxableValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Settlement = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    SupplierLedgerEntryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RecordedByName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SyncState = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpiryClaimCredits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpiryClaimLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiryClaimId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DrugId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PurchaseItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ItemName = table.Column<string>(type: "TEXT", nullable: false),
                    Pack = table.Column<string>(type: "TEXT", nullable: false),
                    HsnCode = table.Column<string>(type: "TEXT", nullable: false),
                    BatchNo = table.Column<string>(type: "TEXT", nullable: false),
                    BatchExpiry = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PurchaseRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxableValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    RejectedQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RejectedValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RejectionReason = table.Column<string>(type: "TEXT", nullable: true),
                    RejectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RejectedByName = table.Column<string>(type: "TEXT", nullable: true),
                    WrittenOffQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WrittenOffAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WrittenOffByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WrittenOffByName = table.Column<string>(type: "TEXT", nullable: true),
                    ReleasedQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreditedValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShortfallValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShortfallReason = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SyncState = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpiryClaimLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpiryClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimNo = table.Column<string>(type: "TEXT", nullable: false),
                    ClaimDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SupplierName = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierAddress = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierGstin = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierPhone = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierStateCode = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierDrugLicence = table.Column<string>(type: "TEXT", nullable: false),
                    PharmacyName = table.Column<string>(type: "TEXT", nullable: false),
                    PharmacyAddress = table.Column<string>(type: "TEXT", nullable: false),
                    PharmacyGstin = table.Column<string>(type: "TEXT", nullable: false),
                    PharmacyPhone = table.Column<string>(type: "TEXT", nullable: false),
                    PharmacyDrugLicence = table.Column<string>(type: "TEXT", nullable: false),
                    PharmacyStateCode = table.Column<string>(type: "TEXT", nullable: false),
                    IsInterState = table.Column<bool>(type: "INTEGER", nullable: false),
                    PolicyDaysBeforeExpiry = table.Column<int>(type: "INTEGER", nullable: false),
                    PolicyDaysAfterExpiry = table.Column<int>(type: "INTEGER", nullable: false),
                    PolicyNote = table.Column<string>(type: "TEXT", nullable: false),
                    TaxableValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IgstAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedByName = table.Column<string>(type: "TEXT", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SentDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    SentVia = table.Column<string>(type: "TEXT", nullable: true),
                    SentTo = table.Column<string>(type: "TEXT", nullable: true),
                    SentByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SentByName = table.Column<string>(type: "TEXT", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    VoidedByName = table.Column<string>(type: "TEXT", nullable: true),
                    VoidReason = table.Column<string>(type: "TEXT", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SyncState = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpiryClaims", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryClaimCreditLines_ExpiryClaimCreditId",
                table: "ExpiryClaimCreditLines",
                column: "ExpiryClaimCreditId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryClaimCreditLines_ExpiryClaimLineId",
                table: "ExpiryClaimCreditLines",
                column: "ExpiryClaimLineId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryClaimCredits_ExpiryClaimId",
                table: "ExpiryClaimCredits",
                column: "ExpiryClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryClaimLines_BatchId_State",
                table: "ExpiryClaimLines",
                columns: new[] { "BatchId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryClaimLines_ExpiryClaimId",
                table: "ExpiryClaimLines",
                column: "ExpiryClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryClaims_ClaimNo",
                table: "ExpiryClaims",
                column: "ClaimNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpiryClaims_SupplierId_Status",
                table: "ExpiryClaims",
                columns: new[] { "SupplierId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpiryClaimCreditLines");

            migrationBuilder.DropTable(
                name: "ExpiryClaimCredits");

            migrationBuilder.DropTable(
                name: "ExpiryClaimLines");

            migrationBuilder.DropTable(
                name: "ExpiryClaims");

            migrationBuilder.DropColumn(
                name: "ExpiryReturnDaysAfter",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ExpiryReturnDaysBefore",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ExpiryReturnPolicyNote",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "ExpiryReturnDaysAfter",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "ExpiryReturnDaysBefore",
                table: "PharmacyProfiles");
        }
    }
}
