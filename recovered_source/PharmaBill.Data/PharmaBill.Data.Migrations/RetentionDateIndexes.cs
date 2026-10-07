using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261005123000_RetentionDateIndexes")]
public class RetentionDateIndexes : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropIndex("IX_Sales_SaleAtUtc", "Sales");
		migrationBuilder.DropIndex("IX_PurchaseInvoices_InvoiceDate", "PurchaseInvoices");
		migrationBuilder.DropIndex("IX_StockMovements_MovementAtUtc", "StockMovements");
		migrationBuilder.DropIndex("IX_WholesaleInvoices_InvoiceAtUtc", "WholesaleInvoices");
		migrationBuilder.CreateIndex("idx_sales_date", "Sales", "SaleAtUtc", null, unique: false, null, new bool[1] { true });
		migrationBuilder.CreateIndex("idx_purchases_date", "PurchaseInvoices", "InvoiceDate", null, unique: false, null, new bool[1] { true });
		migrationBuilder.CreateIndex("idx_stock_movements_date", "StockMovements", "MovementAtUtc", null, unique: false, null, new bool[1] { true });
		migrationBuilder.CreateIndex("idx_wholesale_invoices_date", "WholesaleInvoices", "InvoiceAtUtc", null, unique: false, null, new bool[1] { true });
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropIndex("idx_sales_date", "Sales");
		migrationBuilder.DropIndex("idx_purchases_date", "PurchaseInvoices");
		migrationBuilder.DropIndex("idx_stock_movements_date", "StockMovements");
		migrationBuilder.DropIndex("idx_wholesale_invoices_date", "WholesaleInvoices");
		migrationBuilder.CreateIndex("IX_Sales_SaleAtUtc", "Sales", "SaleAtUtc");
		migrationBuilder.CreateIndex("IX_PurchaseInvoices_InvoiceDate", "PurchaseInvoices", "InvoiceDate");
		migrationBuilder.CreateIndex("IX_StockMovements_MovementAtUtc", "StockMovements", "MovementAtUtc");
		migrationBuilder.CreateIndex("IX_WholesaleInvoices_InvoiceAtUtc", "WholesaleInvoices", "InvoiceAtUtc");
	}
}
