using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261005210000_SocialAuthProviders")]
public class SocialAuthProviders : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<string>("AuthProvider", "AppUsers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("ProviderSubjectId", "AppUsers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.CreateIndex("IX_AppUsers_AuthProvider_ProviderSubjectId", "AppUsers", new string[2] { "AuthProvider", "ProviderSubjectId" });
		migrationBuilder.CreateIndex("IX_AppUsers_Email", "AppUsers", "Email");
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropIndex("IX_AppUsers_AuthProvider_ProviderSubjectId", "AppUsers");
		migrationBuilder.DropIndex("IX_AppUsers_Email", "AppUsers");
		migrationBuilder.DropColumn("AuthProvider", "AppUsers");
		migrationBuilder.DropColumn("ProviderSubjectId", "AppUsers");
	}
}
