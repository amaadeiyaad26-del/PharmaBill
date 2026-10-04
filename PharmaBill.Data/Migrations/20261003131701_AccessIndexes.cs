using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class AccessIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_LicenceRecords_LicenceType_ExpiresOn",
                table: "LicenceRecords",
                columns: new[] { "LicenceType", "ExpiresOn" });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_UserName",
                table: "AppUsers",
                column: "UserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LicenceRecords_LicenceType_ExpiresOn",
                table: "LicenceRecords");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_UserName",
                table: "AppUsers");
        }
    }
}
