using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class TaxDocumentSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerAddress",
                table: "WholesaleInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerGstin",
                table: "WholesaleInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerLegalName",
                table: "WholesaleInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerSnapshotJson",
                table: "WholesaleInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerSnapshotJson",
                table: "WholesaleInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BatchExpiry",
                table: "WholesaleInvoiceItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNo",
                table: "WholesaleInvoiceItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HsnCode",
                table: "WholesaleInvoiceItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "WholesaleInvoiceItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Mrp",
                table: "WholesaleInvoiceItems",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "WholesaleInvoiceItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerSnapshotJson",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PharmacyStateCode",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerSnapshotJson",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "BatchExpiry",
                table: "SaleItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNo",
                table: "SaleItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HsnCode",
                table: "SaleItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "SaleItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerAddress",
                table: "WholesaleInvoices");

            migrationBuilder.DropColumn(
                name: "BuyerGstin",
                table: "WholesaleInvoices");

            migrationBuilder.DropColumn(
                name: "BuyerLegalName",
                table: "WholesaleInvoices");

            migrationBuilder.DropColumn(
                name: "BuyerSnapshotJson",
                table: "WholesaleInvoices");

            migrationBuilder.DropColumn(
                name: "SellerSnapshotJson",
                table: "WholesaleInvoices");

            migrationBuilder.DropColumn(
                name: "BatchExpiry",
                table: "WholesaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "BatchNo",
                table: "WholesaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "HsnCode",
                table: "WholesaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "WholesaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "Mrp",
                table: "WholesaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "WholesaleInvoiceItems");

            migrationBuilder.DropColumn(
                name: "BuyerSnapshotJson",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PharmacyStateCode",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "SellerSnapshotJson",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "BatchExpiry",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "BatchNo",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "HsnCode",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "SaleItems");
        }
    }
}
