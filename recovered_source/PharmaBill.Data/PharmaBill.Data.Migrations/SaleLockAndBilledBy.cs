using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261005153000_SaleLockAndBilledBy")]
public class SaleLockAndBilledBy : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<Guid>("BilledByUserId", "Sales", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<bool>("IsLocked", "Sales", "INTEGER", null, null, rowVersion: false, null, nullable: false, true);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn("BilledByUserId", "Sales");
		migrationBuilder.DropColumn("IsLocked", "Sales");
	}
}
