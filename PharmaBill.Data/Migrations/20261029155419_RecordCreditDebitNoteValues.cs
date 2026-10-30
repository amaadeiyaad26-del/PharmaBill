using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecordCreditDebitNoteValues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BatchExpiry",
                table: "WholesaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNo",
                table: "WholesaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CgstAmount",
                table: "WholesaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "WholesaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HsnCode",
                table: "WholesaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IgstAmount",
                table: "WholesaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "WholesaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SgstAmount",
                table: "WholesaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "WholesaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxableAmount",
                table: "WholesaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "WholesaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "WholesaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValuesBackfilled",
                table: "WholesaleReturnItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BatchExpiry",
                table: "SaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNo",
                table: "SaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CgstAmount",
                table: "SaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "SaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HsnCode",
                table: "SaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IgstAmount",
                table: "SaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "SaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SgstAmount",
                table: "SaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "SaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxableAmount",
                table: "SaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "SaleReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "SaleReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValuesBackfilled",
                table: "SaleReturnItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BuyerAddress",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerGstin",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerLegalName",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerSnapshotJson",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerStateCode",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInterState",
                table: "ReturnNotes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PharmacyStateCode",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReturnDate",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerSnapshotJson",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValuesBackfilled",
                table: "ReturnNotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BuyerSnapshotJson",
                table: "PurchaseReturns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsInterState",
                table: "PurchaseReturns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PharmacyStateCode",
                table: "PurchaseReturns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerAddress",
                table: "PurchaseReturns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerGstin",
                table: "PurchaseReturns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerLegalName",
                table: "PurchaseReturns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerSnapshotJson",
                table: "PurchaseReturns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerStateCode",
                table: "PurchaseReturns",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValuesBackfilled",
                table: "PurchaseReturns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BatchExpiry",
                table: "PurchaseReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNo",
                table: "PurchaseReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CgstAmount",
                table: "PurchaseReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "PurchaseReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HsnCode",
                table: "PurchaseReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "IgstAmount",
                table: "PurchaseReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "PurchaseReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SgstAmount",
                table: "PurchaseReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "PurchaseReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxableAmount",
                table: "PurchaseReturnItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "PurchaseReturnItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ValuesBackfilled",
                table: "PurchaseReturnItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatchExpiry",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "BatchNo",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "CgstAmount",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "HsnCode",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "IgstAmount",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "SgstAmount",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "TaxableAmount",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "ValuesBackfilled",
                table: "WholesaleReturnItems");

            migrationBuilder.DropColumn(
                name: "BatchExpiry",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "BatchNo",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "CgstAmount",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "HsnCode",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "IgstAmount",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "SgstAmount",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "TaxableAmount",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "ValuesBackfilled",
                table: "SaleReturnItems");

            migrationBuilder.DropColumn(
                name: "BuyerAddress",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "BuyerGstin",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "BuyerLegalName",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "BuyerSnapshotJson",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "BuyerStateCode",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "IsInterState",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "PharmacyStateCode",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "ReturnDate",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "SellerSnapshotJson",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "ValuesBackfilled",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "BuyerSnapshotJson",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "IsInterState",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "PharmacyStateCode",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "SellerAddress",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "SellerGstin",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "SellerLegalName",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "SellerSnapshotJson",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "SellerStateCode",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "ValuesBackfilled",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "BatchExpiry",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "BatchNo",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "CgstAmount",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "HsnCode",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "IgstAmount",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "SgstAmount",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "TaxableAmount",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "ValuesBackfilled",
                table: "PurchaseReturnItems");
        }
    }
}
