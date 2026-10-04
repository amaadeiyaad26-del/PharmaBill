using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaBill.Data.Migrations
{
    /// <inheritdoc />
    public partial class FirstRunSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE PharmacyProfiles
                SET BusinessMode = CASE UPPER(BusinessMode)
                    WHEN 'RETAIL' THEN '0'
                    WHEN 'WHOLESALER' THEN '1'
                    WHEN 'BOTH' THEN '2'
                    ELSE '0'
                END;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "BusinessMode",
                table: "PharmacyProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountName",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankIfsc",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ClockRollbackDetected",
                table: "PharmacyProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CompetentPersonName",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompetentPersonQualification",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompetentPersonRegistrationNumber",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoicePrefix",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "WIN1");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastEntitlementCheckAtUtc",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pan",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialStartedAtUtc",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpiId",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WholesaleLicenceTypesJson",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "DocumentPath",
                table: "LicenceRecords",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AppUsers",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IdleLockMinutes",
                table: "AppUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<bool>(
                name: "UseWindowsHello",
                table: "AppUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankAccountName",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "BankIfsc",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "ClockRollbackDetected",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "CompetentPersonName",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "CompetentPersonQualification",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "CompetentPersonRegistrationNumber",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "InvoicePrefix",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "LastEntitlementCheckAtUtc",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "Pan",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "TrialStartedAtUtc",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "UpiId",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "WholesaleLicenceTypesJson",
                table: "PharmacyProfiles");

            migrationBuilder.DropColumn(
                name: "DocumentPath",
                table: "LicenceRecords");

            migrationBuilder.DropColumn(
                name: "IdleLockMinutes",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "UseWindowsHello",
                table: "AppUsers");

            migrationBuilder.AlterColumn<string>(
                name: "BusinessMode",
                table: "PharmacyProfiles",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<string>(
                name: "PasswordHash",
                table: "AppUsers",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }
    }
}
