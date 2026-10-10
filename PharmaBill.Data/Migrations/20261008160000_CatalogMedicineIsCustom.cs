using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

#nullable disable

namespace PharmaBill.Data.Migrations;

/// <summary>
/// Records the custom-medicine column in migration history.
/// DbInitializer adds the column only when PRAGMA table_info shows it is missing.
/// </summary>
[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261008160000_CatalogMedicineIsCustom")]
public partial class CatalogMedicineIsCustom : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
	}
}
