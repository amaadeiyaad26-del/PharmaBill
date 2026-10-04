using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class MedicineCatalogImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE VIRTUAL TABLE CatalogMedicineFts USING fts5(
                    MedicineId UNINDEXED,
                    Name,
                    Composition,
                    Manufacturer,
                    tokenize='unicode61 remove_diacritics 2'
                );
                """);

            migrationBuilder.AddColumn<string>(
                name: "CompositionKey",
                table: "CatalogMedicines",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsDiscontinued",
                table: "CatalogMedicines",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PackSizeLabel",
                table: "CatalogMedicines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferencePrice",
                table: "CatalogMedicines",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortComposition1",
                table: "CatalogMedicines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortComposition2",
                table: "CatalogMedicines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "CatalogMedicines",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActionClass",
                table: "CatalogInfos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChemicalClass",
                table: "CatalogInfos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NameKey",
                table: "CatalogInfos",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TherapeuticClass",
                table: "CatalogInfos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Use",
                table: "CatalogInfos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CatalogImportStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ImportKey = table.Column<string>(type: "TEXT", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", nullable: false),
                    FileFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    LastProcessedRecord = table.Column<long>(type: "INTEGER", nullable: false),
                    ImportedRows = table.Column<long>(type: "INTEGER", nullable: false),
                    SkippedRows = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SyncState = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogImportStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMedicines_CompositionKey",
                table: "CatalogMedicines",
                column: "CompositionKey");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogMedicines_SourceId",
                table: "CatalogMedicines",
                column: "SourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogInfos_NameKey",
                table: "CatalogInfos",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogImportStates_ImportKey",
                table: "CatalogImportStates",
                column: "ImportKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS CatalogMedicineFts;");

            migrationBuilder.DropTable(
                name: "CatalogImportStates");

            migrationBuilder.DropIndex(
                name: "IX_CatalogMedicines_CompositionKey",
                table: "CatalogMedicines");

            migrationBuilder.DropIndex(
                name: "IX_CatalogMedicines_SourceId",
                table: "CatalogMedicines");

            migrationBuilder.DropIndex(
                name: "IX_CatalogInfos_NameKey",
                table: "CatalogInfos");

            migrationBuilder.DropColumn(
                name: "CompositionKey",
                table: "CatalogMedicines");

            migrationBuilder.DropColumn(
                name: "IsDiscontinued",
                table: "CatalogMedicines");

            migrationBuilder.DropColumn(
                name: "PackSizeLabel",
                table: "CatalogMedicines");

            migrationBuilder.DropColumn(
                name: "ReferencePrice",
                table: "CatalogMedicines");

            migrationBuilder.DropColumn(
                name: "ShortComposition1",
                table: "CatalogMedicines");

            migrationBuilder.DropColumn(
                name: "ShortComposition2",
                table: "CatalogMedicines");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "CatalogMedicines");

            migrationBuilder.DropColumn(
                name: "ActionClass",
                table: "CatalogInfos");

            migrationBuilder.DropColumn(
                name: "ChemicalClass",
                table: "CatalogInfos");

            migrationBuilder.DropColumn(
                name: "NameKey",
                table: "CatalogInfos");

            migrationBuilder.DropColumn(
                name: "TherapeuticClass",
                table: "CatalogInfos");

            migrationBuilder.DropColumn(
                name: "Use",
                table: "CatalogInfos");
        }
    }
}
