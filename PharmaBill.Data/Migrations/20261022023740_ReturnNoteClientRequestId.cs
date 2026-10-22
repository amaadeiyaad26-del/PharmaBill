using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReturnNoteClientRequestId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientRequestId",
                table: "ReturnNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReturnNotes_ClientRequestId",
                table: "ReturnNotes",
                column: "ClientRequestId",
                unique: true,
                filter: "\"ClientRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReturnNotes_ClientRequestId",
                table: "ReturnNotes");

            migrationBuilder.DropColumn(
                name: "ClientRequestId",
                table: "ReturnNotes");
        }
    }
}
