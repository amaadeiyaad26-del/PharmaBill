using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class SaleClientRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "Sales",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ClientRequestId",
                table: "Sales",
                column: "ClientRequestId",
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sales_ClientRequestId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "Sales");
        }
    }
}
