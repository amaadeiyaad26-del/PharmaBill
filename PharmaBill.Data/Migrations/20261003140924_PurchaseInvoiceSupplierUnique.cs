using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseInvoiceSupplierUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_InvoiceNo",
                table: "PurchaseInvoices");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_SupplierId_InvoiceNo",
                table: "PurchaseInvoices",
                columns: new[] { "SupplierId", "InvoiceNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_SupplierId_InvoiceNo",
                table: "PurchaseInvoices");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_InvoiceNo",
                table: "PurchaseInvoices",
                column: "InvoiceNo");
        }
    }
}
