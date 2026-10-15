using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <summary>
    /// Adds Drugs.IsNilRated. Existing rows get false, so a stored 0% that nobody marked Nil-rated is treated as
    /// &quot;rate not known&quot; and blocks billing until someone enters the rate. No rate is rewritten or guessed here.
    /// </summary>
    public partial class ProductNilRated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsNilRated",
                table: "Drugs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsNilRated",
                table: "Drugs");
        }
    }
}
