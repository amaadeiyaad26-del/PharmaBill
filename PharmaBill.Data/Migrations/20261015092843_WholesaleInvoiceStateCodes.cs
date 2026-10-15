using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class WholesaleInvoiceStateCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerStateCode",
                table: "WholesaleInvoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PharmacyStateCode",
                table: "WholesaleInvoices",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerStateCode",
                table: "WholesaleInvoices");

            migrationBuilder.DropColumn(
                name: "PharmacyStateCode",
                table: "WholesaleInvoices");
        }
    }
}
