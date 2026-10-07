using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261006120000_UserManualEmailFlags")]
public class UserManualEmailFlags : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<bool>("UserManualEmailSent", "PharmacyProfiles", "INTEGER", null, null, rowVersion: false, null, nullable: false, false);
		migrationBuilder.AddColumn<DateTime>("UserManualSentAtUtc", "PharmacyProfiles", "TEXT", null, null, rowVersion: false, null, nullable: true);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropColumn("UserManualEmailSent", "PharmacyProfiles");
		migrationBuilder.DropColumn("UserManualSentAtUtc", "PharmacyProfiles");
	}
}
