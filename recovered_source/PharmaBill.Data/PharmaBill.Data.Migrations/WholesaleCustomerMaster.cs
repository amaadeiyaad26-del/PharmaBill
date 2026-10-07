using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261003152100_WholesaleCustomerMaster")]
public class WholesaleCustomerMaster : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<string>("State", "PharmacyProfiles", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("WholesaleBuyerLicenceRulesJson", "PharmacyProfiles", "TEXT", null, null, rowVersion: false, null, nullable: false, "{}");
		migrationBuilder.AddColumn<int>("CreditDays", "Customers", "INTEGER", null, null, rowVersion: false, null, nullable: false, 0);
		migrationBuilder.AddColumn<decimal>("OpeningBalance", "Customers", "decimal(18,2)", null, null, rowVersion: false, null, nullable: false, 0m);
		migrationBuilder.AddColumn<string>("PriceCategory", "Customers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("Route", "Customers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("Salesman", "Customers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("State", "Customers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("StateDrugControlPortalUrl", "Customers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("VerifiedBy", "Customers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<DateTime>("VerifiedOnUtc", "Customers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("VerificationMethod", "Customers", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("DocumentPath", "CustomerLicences", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("IssuingAuthority", "CustomerLicences", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<DateOnly>("IssuedOn", "CustomerLicences", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.CreateIndex("IX_Customers_Gstin", "Customers", "Gstin");
		migrationBuilder.CreateIndex("IX_CustomerLicences_LicenceNumber", "CustomerLicences", "LicenceNumber");
		migrationBuilder.CreateIndex("IX_CustomerLicences_CustomerId_ExpiresOn", "CustomerLicences", new string[2] { "CustomerId", "ExpiresOn" });
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropIndex("IX_Customers_Gstin", "Customers");
		migrationBuilder.DropIndex("IX_CustomerLicences_LicenceNumber", "CustomerLicences");
		migrationBuilder.DropIndex("IX_CustomerLicences_CustomerId_ExpiresOn", "CustomerLicences");
		migrationBuilder.DropColumn("CreditDays", "Customers");
		migrationBuilder.DropColumn("OpeningBalance", "Customers");
		migrationBuilder.DropColumn("PriceCategory", "Customers");
		migrationBuilder.DropColumn("Route", "Customers");
		migrationBuilder.DropColumn("Salesman", "Customers");
		migrationBuilder.DropColumn("State", "Customers");
		migrationBuilder.DropColumn("StateDrugControlPortalUrl", "Customers");
		migrationBuilder.DropColumn("VerifiedBy", "Customers");
		migrationBuilder.DropColumn("VerifiedOnUtc", "Customers");
		migrationBuilder.DropColumn("VerificationMethod", "Customers");
		migrationBuilder.DropColumn("DocumentPath", "CustomerLicences");
		migrationBuilder.DropColumn("IssuingAuthority", "CustomerLicences");
		migrationBuilder.DropColumn("IssuedOn", "CustomerLicences");
		migrationBuilder.DropColumn("State", "PharmacyProfiles");
		migrationBuilder.DropColumn("WholesaleBuyerLicenceRulesJson", "PharmacyProfiles");
	}
}
