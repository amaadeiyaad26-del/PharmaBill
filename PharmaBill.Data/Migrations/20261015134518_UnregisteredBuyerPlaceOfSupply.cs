using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnregisteredBuyerPlaceOfSupply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BuyerStateDefaulted",
                table: "WholesaleInvoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "B2cLargeInvoiceThreshold",
                table: "PharmacyProfiles",
                type: "decimal(18,2)",
                nullable: false,
                // Existing pharmacies get the current B2C-large limit (₹1,00,000; Notification 12/2024–CT).
                defaultValue: 100000m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerStateDefaulted",
                table: "WholesaleInvoices");

            migrationBuilder.DropColumn(
                name: "B2cLargeInvoiceThreshold",
                table: "PharmacyProfiles");
        }
    }
}
