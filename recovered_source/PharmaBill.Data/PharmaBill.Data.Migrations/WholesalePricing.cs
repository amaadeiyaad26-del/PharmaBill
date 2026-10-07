using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261003163500_WholesalePricing")]
public class WholesalePricing : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<decimal>("Pts", "Batches", "decimal(18,2)", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.CreateTable("DrugPackLevels", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> level = table.Column<int>("INTEGER");
			OperationBuilder<AddColumnOperation> label = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> unitsPerPack = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				DrugId = drugId,
				Level = level,
				Label = label,
				UnitsPerPack = unitsPerPack,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_DrugPackLevels", x => x.Id);
		});
		migrationBuilder.CreateTable("CustomerCategoryPrices", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> priceCategory = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> unitPrice = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> effectiveFrom = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> effectiveTo = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				DrugId = drugId,
				PriceCategory = priceCategory,
				UnitPrice = unitPrice,
				EffectiveFrom = effectiveFrom,
				EffectiveTo = effectiveTo,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_CustomerCategoryPrices", x => x.Id);
		});
		migrationBuilder.CreateTable("TradeDiscounts", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> priceCategory = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> discountPercent = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> effectiveFrom = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> effectiveTo = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				DrugId = drugId,
				PriceCategory = priceCategory,
				DiscountPercent = discountPercent,
				EffectiveFrom = effectiveFrom,
				EffectiveTo = effectiveTo,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_TradeDiscounts", x => x.Id);
		});
		migrationBuilder.CreateTable("WholesaleSchemes", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> priceCategory = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> buyQuantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> freeQuantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> startsOn = table.Column<DateOnly>("TEXT");
			OperationBuilder<AddColumnOperation> endsOn = table.Column<DateOnly>("TEXT");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				DrugId = drugId,
				PriceCategory = priceCategory,
				BuyQuantity = buyQuantity,
				FreeQuantity = freeQuantity,
				StartsOn = startsOn,
				EndsOn = endsOn,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_WholesaleSchemes", x => x.Id);
		});
		migrationBuilder.CreateTable("WholesaleRateHistory", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> customerId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> batchId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> invoiceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> soldAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> unitRate = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> quantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				CustomerId = customerId,
				DrugId = drugId,
				BatchId = batchId,
				InvoiceId = invoiceId,
				SoldAtUtc = soldAtUtc,
				UnitRate = unitRate,
				Quantity = quantity,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_WholesaleRateHistory", x => x.Id);
		});
		migrationBuilder.CreateIndex("IX_DrugPackLevels_DrugId_Level", "DrugPackLevels", new string[2] { "DrugId", "Level" }, null, unique: true);
		migrationBuilder.CreateIndex("IX_CustomerCategoryPrices_DrugId_PriceCategory_EffectiveFrom", "CustomerCategoryPrices", new string[3] { "DrugId", "PriceCategory", "EffectiveFrom" });
		migrationBuilder.CreateIndex("IX_TradeDiscounts_PriceCategory_EffectiveFrom", "TradeDiscounts", new string[2] { "PriceCategory", "EffectiveFrom" });
		migrationBuilder.CreateIndex("IX_WholesaleSchemes_DrugId_PriceCategory_StartsOn_EndsOn", "WholesaleSchemes", new string[4] { "DrugId", "PriceCategory", "StartsOn", "EndsOn" });
		migrationBuilder.CreateIndex("IX_WholesaleRateHistory_CustomerId_DrugId_SoldAtUtc", "WholesaleRateHistory", new string[3] { "CustomerId", "DrugId", "SoldAtUtc" });
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable("CustomerCategoryPrices");
		migrationBuilder.DropTable("DrugPackLevels");
		migrationBuilder.DropTable("TradeDiscounts");
		migrationBuilder.DropTable("WholesaleRateHistory");
		migrationBuilder.DropTable("WholesaleSchemes");
		migrationBuilder.DropColumn("Pts", "Batches");
	}
}
