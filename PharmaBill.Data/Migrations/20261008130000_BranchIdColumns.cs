using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

#nullable disable

namespace PharmaBill.Data.Migrations;

/// <summary>
/// Records BranchId schema intent in __EFMigrationsHistory.
/// Actual ADD COLUMN work is done idempotently by DbInitializer (PRAGMA check + safe ALTER)
/// so MigrateAsync never throws "duplicate column name: BranchId" on existing DBs.
/// </summary>
[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261008130000_BranchIdColumns")]
public partial class BranchIdColumns : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		// No-op: DbInitializer.EnsureBranchIdColumnsAsync / TryAddAuditLogsBranchIdAsync
		// add BranchId only when PRAGMA table_info shows it is missing.
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
	}
}