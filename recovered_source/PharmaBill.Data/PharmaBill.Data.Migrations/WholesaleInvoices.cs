using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261003165000_WholesaleInvoices")]
public class WholesaleInvoices : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<decimal>("CgstAmount", "WholesaleInvoices", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<decimal>("SgstAmount", "WholesaleInvoices", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<decimal>("IgstAmount", "WholesaleInvoices", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<decimal>("RoundOff", "WholesaleInvoices", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<string>("TransportDetails", "WholesaleInvoices", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("VehicleNumber", "WholesaleInvoices", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("EWayBillNumber", "WholesaleInvoices", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("Irn", "WholesaleInvoices", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("Status", "WholesaleInvoices", "TEXT", null, null, rowVersion: false, null, nullable: false, "Posted");
		migrationBuilder.AddColumn<DateTime>("CancelledAtUtc", "WholesaleInvoices", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("CancellationReason", "WholesaleInvoices", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<decimal>("FreeQuantity", "WholesaleInvoiceItems", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<decimal>("TaxableAmount", "WholesaleInvoiceItems", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<decimal>("CgstAmount", "WholesaleInvoiceItems", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<decimal>("SgstAmount", "WholesaleInvoiceItems", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<decimal>("IgstAmount", "WholesaleInvoiceItems", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<Guid>("CustomerId", "ScheduleRegisterEntries", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("BuyerName", "ScheduleRegisterEntries", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("BuyerAddress", "ScheduleRegisterEntries", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("BuyerPhone", "ScheduleRegisterEntries", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("BuyerLicenceNumber", "ScheduleRegisterEntries", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<Guid>("WholesaleInvoiceId", "Receipts", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("ChequeNumber", "Receipts", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<DateOnly>("ChequeDate", "Receipts", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("BankName", "Receipts", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.CreateIndex("IX_Receipts_WholesaleInvoiceId", "Receipts", "WholesaleInvoiceId");
		migrationBuilder.CreateIndex("IX_WholesaleInvoices_Status", "WholesaleInvoices", "Status");
		migrationBuilder.CreateTable("WholesaleReturnItems", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> returnNoteId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> wholesaleInvoiceItemId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> batchId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> quantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> creditAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> restocked = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> quarantined = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				ReturnNoteId = returnNoteId,
				WholesaleInvoiceItemId = wholesaleInvoiceItemId,
				BatchId = batchId,
				DrugId = drugId,
				Quantity = quantity,
				CreditAmount = creditAmount,
				Restocked = restocked,
				Quarantined = quarantined,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_WholesaleReturnItems", x => x.Id);
		});
		migrationBuilder.CreateIndex("IX_WholesaleReturnItems_ReturnNoteId", "WholesaleReturnItems", "ReturnNoteId");
		migrationBuilder.CreateIndex("IX_WholesaleReturnItems_WholesaleInvoiceItemId", "WholesaleReturnItems", "WholesaleInvoiceItemId");
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable("WholesaleReturnItems");
		migrationBuilder.DropColumn("CgstAmount", "WholesaleInvoices");
		migrationBuilder.DropColumn("SgstAmount", "WholesaleInvoices");
		migrationBuilder.DropColumn("IgstAmount", "WholesaleInvoices");
		migrationBuilder.DropColumn("RoundOff", "WholesaleInvoices");
		migrationBuilder.DropColumn("TransportDetails", "WholesaleInvoices");
		migrationBuilder.DropColumn("VehicleNumber", "WholesaleInvoices");
		migrationBuilder.DropColumn("EWayBillNumber", "WholesaleInvoices");
		migrationBuilder.DropColumn("Irn", "WholesaleInvoices");
		migrationBuilder.DropColumn("Status", "WholesaleInvoices");
		migrationBuilder.DropColumn("CancelledAtUtc", "WholesaleInvoices");
		migrationBuilder.DropColumn("CancellationReason", "WholesaleInvoices");
		migrationBuilder.DropColumn("FreeQuantity", "WholesaleInvoiceItems");
		migrationBuilder.DropColumn("TaxableAmount", "WholesaleInvoiceItems");
		migrationBuilder.DropColumn("CgstAmount", "WholesaleInvoiceItems");
		migrationBuilder.DropColumn("SgstAmount", "WholesaleInvoiceItems");
		migrationBuilder.DropColumn("IgstAmount", "WholesaleInvoiceItems");
		migrationBuilder.DropColumn("CustomerId", "ScheduleRegisterEntries");
		migrationBuilder.DropColumn("BuyerName", "ScheduleRegisterEntries");
		migrationBuilder.DropColumn("BuyerAddress", "ScheduleRegisterEntries");
		migrationBuilder.DropColumn("BuyerPhone", "ScheduleRegisterEntries");
		migrationBuilder.DropColumn("BuyerLicenceNumber", "ScheduleRegisterEntries");
		migrationBuilder.DropIndex("IX_Receipts_WholesaleInvoiceId", "Receipts");
		migrationBuilder.DropIndex("IX_WholesaleInvoices_Status", "WholesaleInvoices");
		migrationBuilder.DropColumn("WholesaleInvoiceId", "Receipts");
		migrationBuilder.DropColumn("ChequeNumber", "Receipts");
		migrationBuilder.DropColumn("ChequeDate", "Receipts");
		migrationBuilder.DropColumn("BankName", "Receipts");
	}
}
