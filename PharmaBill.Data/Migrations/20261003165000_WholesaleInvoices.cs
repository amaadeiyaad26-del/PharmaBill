using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

#nullable disable

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261003165000_WholesaleInvoices")]
public partial class WholesaleInvoices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("CgstAmount", "WholesaleInvoices", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("SgstAmount", "WholesaleInvoices", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("IgstAmount", "WholesaleInvoices", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("RoundOff", "WholesaleInvoices", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<string>("TransportDetails", "WholesaleInvoices", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("VehicleNumber", "WholesaleInvoices", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("EWayBillNumber", "WholesaleInvoices", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("Irn", "WholesaleInvoices", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("Status", "WholesaleInvoices", type: "TEXT", nullable: false, defaultValue: "Posted");
        migrationBuilder.AddColumn<DateTime>("CancelledAtUtc", "WholesaleInvoices", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("CancellationReason", "WholesaleInvoices", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<decimal>("FreeQuantity", "WholesaleInvoiceItems", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("TaxableAmount", "WholesaleInvoiceItems", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("CgstAmount", "WholesaleInvoiceItems", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("SgstAmount", "WholesaleInvoiceItems", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("IgstAmount", "WholesaleInvoiceItems", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<Guid>("CustomerId", "ScheduleRegisterEntries", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("BuyerName", "ScheduleRegisterEntries", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("BuyerAddress", "ScheduleRegisterEntries", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("BuyerPhone", "ScheduleRegisterEntries", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("BuyerLicenceNumber", "ScheduleRegisterEntries", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<Guid>("WholesaleInvoiceId", "Receipts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("ChequeNumber", "Receipts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateOnly>("ChequeDate", "Receipts", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>("BankName", "Receipts", type: "TEXT", nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_Receipts_WholesaleInvoiceId",
            table: "Receipts",
            column: "WholesaleInvoiceId");
        migrationBuilder.CreateIndex(
            name: "IX_WholesaleInvoices_Status",
            table: "WholesaleInvoices",
            column: "Status");
        migrationBuilder.CreateTable(
            name: "WholesaleReturnItems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                ReturnNoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                WholesaleInvoiceItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                DrugId = table.Column<Guid>(type: "TEXT", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                CreditAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Restocked = table.Column<bool>(type: "INTEGER", nullable: false),
                Quarantined = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                DeviceId = table.Column<Guid>(type: "TEXT", nullable: false),
                IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                HlcStamp = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                SyncState = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_WholesaleReturnItems", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_WholesaleReturnItems_ReturnNoteId",
            table: "WholesaleReturnItems",
            column: "ReturnNoteId");
        migrationBuilder.CreateIndex(
            name: "IX_WholesaleReturnItems_WholesaleInvoiceItemId",
            table: "WholesaleReturnItems",
            column: "WholesaleInvoiceItemId");
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
