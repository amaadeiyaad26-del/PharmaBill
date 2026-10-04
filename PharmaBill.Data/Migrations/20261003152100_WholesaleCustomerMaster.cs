using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

#nullable disable

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261003152100_WholesaleCustomerMaster")]
public partial class WholesaleCustomerMaster : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "State",
            table: "PharmacyProfiles",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "WholesaleBuyerLicenceRulesJson",
            table: "PharmacyProfiles",
            type: "TEXT",
            nullable: false,
            defaultValue: "{}");
        migrationBuilder.AddColumn<int>(
            name: "CreditDays",
            table: "Customers",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<decimal>(
            name: "OpeningBalance",
            table: "Customers",
            type: "decimal(18,2)",
            nullable: false,
            defaultValue: 0m);
        migrationBuilder.AddColumn<string>(
            name: "PriceCategory",
            table: "Customers",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "Route",
            table: "Customers",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "Salesman",
            table: "Customers",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "State",
            table: "Customers",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "StateDrugControlPortalUrl",
            table: "Customers",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "VerifiedBy",
            table: "Customers",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "VerifiedOnUtc",
            table: "Customers",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "VerificationMethod",
            table: "Customers",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "DocumentPath",
            table: "CustomerLicences",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "IssuingAuthority",
            table: "CustomerLicences",
            type: "TEXT",
            nullable: true);
        migrationBuilder.AddColumn<DateOnly>(
            name: "IssuedOn",
            table: "CustomerLicences",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Customers_Gstin",
            table: "Customers",
            column: "Gstin");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerLicences_LicenceNumber",
            table: "CustomerLicences",
            column: "LicenceNumber");
        migrationBuilder.CreateIndex(
            name: "IX_CustomerLicences_CustomerId_ExpiresOn",
            table: "CustomerLicences",
            columns: new[] { "CustomerId", "ExpiresOn" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Customers_Gstin", table: "Customers");
        migrationBuilder.DropIndex(name: "IX_CustomerLicences_LicenceNumber", table: "CustomerLicences");
        migrationBuilder.DropIndex(name: "IX_CustomerLicences_CustomerId_ExpiresOn", table: "CustomerLicences");
        migrationBuilder.DropColumn(name: "CreditDays", table: "Customers");
        migrationBuilder.DropColumn(name: "OpeningBalance", table: "Customers");
        migrationBuilder.DropColumn(name: "PriceCategory", table: "Customers");
        migrationBuilder.DropColumn(name: "Route", table: "Customers");
        migrationBuilder.DropColumn(name: "Salesman", table: "Customers");
        migrationBuilder.DropColumn(name: "State", table: "Customers");
        migrationBuilder.DropColumn(name: "StateDrugControlPortalUrl", table: "Customers");
        migrationBuilder.DropColumn(name: "VerifiedBy", table: "Customers");
        migrationBuilder.DropColumn(name: "VerifiedOnUtc", table: "Customers");
        migrationBuilder.DropColumn(name: "VerificationMethod", table: "Customers");
        migrationBuilder.DropColumn(name: "DocumentPath", table: "CustomerLicences");
        migrationBuilder.DropColumn(name: "IssuingAuthority", table: "CustomerLicences");
        migrationBuilder.DropColumn(name: "IssuedOn", table: "CustomerLicences");
        migrationBuilder.DropColumn(name: "State", table: "PharmacyProfiles");
        migrationBuilder.DropColumn(name: "WholesaleBuyerLicenceRulesJson", table: "PharmacyProfiles");
    }
}
