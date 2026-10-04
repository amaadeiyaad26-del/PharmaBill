using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

#nullable disable

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261003163500_WholesalePricing")]
public partial class WholesalePricing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "Pts",
            table: "Batches",
            type: "decimal(18,2)",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "DrugPackLevels",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DrugId = table.Column<Guid>(type: "TEXT", nullable: false),
                Level = table.Column<int>(type: "INTEGER", nullable: false),
                Label = table.Column<string>(type: "TEXT", nullable: false),
                UnitsPerPack = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                SyncState = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_DrugPackLevels", x => x.Id));

        migrationBuilder.CreateTable(
            name: "CustomerCategoryPrices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DrugId = table.Column<Guid>(type: "TEXT", nullable: true),
                PriceCategory = table.Column<string>(type: "TEXT", nullable: false),
                UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                SyncState = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CustomerCategoryPrices", x => x.Id));

        migrationBuilder.CreateTable(
            name: "TradeDiscounts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DrugId = table.Column<Guid>(type: "TEXT", nullable: true),
                PriceCategory = table.Column<string>(type: "TEXT", nullable: false),
                DiscountPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                EffectiveFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                EffectiveTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                SyncState = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_TradeDiscounts", x => x.Id));

        migrationBuilder.CreateTable(
            name: "WholesaleSchemes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                DrugId = table.Column<Guid>(type: "TEXT", nullable: true),
                PriceCategory = table.Column<string>(type: "TEXT", nullable: true),
                BuyQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                FreeQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                StartsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                SyncState = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_WholesaleSchemes", x => x.Id));

        migrationBuilder.CreateTable(
            name: "WholesaleRateHistory",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                DrugId = table.Column<Guid>(type: "TEXT", nullable: false),
                BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                InvoiceId = table.Column<Guid>(type: "TEXT", nullable: false),
                SoldAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UnitRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                SyncState = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_WholesaleRateHistory", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_DrugPackLevels_DrugId_Level",
            table: "DrugPackLevels",
            columns: new[] { "DrugId", "Level" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_CustomerCategoryPrices_DrugId_PriceCategory_EffectiveFrom",
            table: "CustomerCategoryPrices",
            columns: new[] { "DrugId", "PriceCategory", "EffectiveFrom" });
        migrationBuilder.CreateIndex(
            name: "IX_TradeDiscounts_PriceCategory_EffectiveFrom",
            table: "TradeDiscounts",
            columns: new[] { "PriceCategory", "EffectiveFrom" });
        migrationBuilder.CreateIndex(
            name: "IX_WholesaleSchemes_DrugId_PriceCategory_StartsOn_EndsOn",
            table: "WholesaleSchemes",
            columns: new[] { "DrugId", "PriceCategory", "StartsOn", "EndsOn" });
        migrationBuilder.CreateIndex(
            name: "IX_WholesaleRateHistory_CustomerId_DrugId_SoldAtUtc",
            table: "WholesaleRateHistory",
            columns: new[] { "CustomerId", "DrugId", "SoldAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CustomerCategoryPrices");
        migrationBuilder.DropTable(name: "DrugPackLevels");
        migrationBuilder.DropTable(name: "TradeDiscounts");
        migrationBuilder.DropTable(name: "WholesaleRateHistory");
        migrationBuilder.DropTable(name: "WholesaleSchemes");
        migrationBuilder.DropColumn(name: "Pts", table: "Batches");
    }
}
