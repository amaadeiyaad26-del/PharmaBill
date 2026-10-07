using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Migrations;

[DbContext(typeof(PharmaBillDbContext))]
[Migration("20261003130446_InitialCreate")]
public class InitialCreate : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.CreateTable("AppUsers", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> displayName = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> userName = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> passwordHash = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> phone = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> email = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> role = table.Column<int>("INTEGER");
			OperationBuilder<AddColumnOperation> isActive = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> lastLoginAtUtc = table.Column<DateTime>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				DisplayName = displayName,
				UserName = userName,
				PasswordHash = passwordHash,
				Phone = phone,
				Email = email,
				Role = role,
				IsActive = isActive,
				LastLoginAtUtc = lastLoginAtUtc,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_AppUsers", x => x.Id);
		});
		migrationBuilder.CreateTable("AuditLogs", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> userId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> actionAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> action = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> entityName = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> entityId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> details = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				UserId = userId,
				ActionAtUtc = actionAtUtc,
				Action = action,
				EntityName = entityName,
				EntityId = entityId,
				Details = details,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_AuditLogs", x => x.Id);
		});
		migrationBuilder.CreateTable("Batches", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> supplierId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> batchNo = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> expiryDate = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> quantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> purchasePrice = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> salePrice = table.Column<decimal>("decimal(18,2)", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> mrp = table.Column<decimal>("decimal(18,2)", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> rack = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				DrugId = drugId,
				SupplierId = supplierId,
				BatchNo = batchNo,
				ExpiryDate = expiryDate,
				Quantity = quantity,
				PurchasePrice = purchasePrice,
				SalePrice = salePrice,
				Mrp = mrp,
				Rack = rack,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_Batches", x => x.Id);
		});
		migrationBuilder.CreateTable("CatalogInfos", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> catalogMedicineId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> schedule = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> isHabitForming = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> requiresPrescription = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> registerType = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				CatalogMedicineId = catalogMedicineId,
				Schedule = schedule,
				IsHabitForming = isHabitForming,
				RequiresPrescription = requiresPrescription,
				RegisterType = registerType,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_CatalogInfos", x => x.Id);
		});
		migrationBuilder.CreateTable("CatalogMedicines", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> name = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> genericName = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> brandName = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> strength = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> dosageForm = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> manufacturer = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> sourceId = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				Name = name,
				GenericName = genericName,
				BrandName = brandName,
				Strength = strength,
				DosageForm = dosageForm,
				Manufacturer = manufacturer,
				SourceId = sourceId,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_CatalogMedicines", x => x.Id);
		});
		migrationBuilder.CreateTable("ChangeLogs", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> entityName = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> entityId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> operation = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> changedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> payload = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				EntityName = entityName,
				EntityId = entityId,
				Operation = operation,
				ChangedAtUtc = changedAtUtc,
				Payload = payload,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_ChangeLogs", x => x.Id);
		});
		migrationBuilder.CreateTable("CustomerLedgerEntries", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> customerId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> entryAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> entryType = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> referenceId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> referenceNo = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> debit = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> credit = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				CustomerId = customerId,
				EntryAtUtc = entryAtUtc,
				EntryType = entryType,
				ReferenceId = referenceId,
				ReferenceNo = referenceNo,
				Debit = debit,
				Credit = credit,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_CustomerLedgerEntries", x => x.Id);
		});
		migrationBuilder.CreateTable("CustomerLicences", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> customerId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> licenceNumber = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> licenceType = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> expiresOn = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> authorisation = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				CustomerId = customerId,
				LicenceNumber = licenceNumber,
				LicenceType = licenceType,
				ExpiresOn = expiresOn,
				Authorisation = authorisation,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_CustomerLicences", x => x.Id);
		});
		migrationBuilder.CreateTable("Customers", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> name = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> phone = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> email = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> address = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> gstin = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> buyerType = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> isActive = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> creditLimit = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				Name = name,
				Phone = phone,
				Email = email,
				Address = address,
				Gstin = gstin,
				BuyerType = buyerType,
				IsActive = isActive,
				CreditLimit = creditLimit,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_Customers", x => x.Id);
		});
		migrationBuilder.CreateTable("DeviceInfos", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> deviceName = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> platform = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> appVersion = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> lastSyncAtUtc = table.Column<DateTime>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> isCurrentDevice = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				DeviceName = deviceName,
				Platform = platform,
				AppVersion = appVersion,
				LastSyncAtUtc = lastSyncAtUtc,
				IsCurrentDevice = isCurrentDevice,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_DeviceInfos", x => x.Id);
		});
		migrationBuilder.CreateTable("Drugs", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> name = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> genericName = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> brandName = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> strength = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> dosageForm = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> unit = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> barcode = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> schedule = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> hsnCode = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> gstRate = table.Column<decimal>("decimal(18,2)", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> mrp = table.Column<decimal>("decimal(18,2)", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> salePrice = table.Column<decimal>("decimal(18,2)", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> catalogMedicineId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> isActive = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				Name = name,
				GenericName = genericName,
				BrandName = brandName,
				Strength = strength,
				DosageForm = dosageForm,
				Unit = unit,
				Barcode = barcode,
				Schedule = schedule,
				HsnCode = hsnCode,
				GstRate = gstRate,
				Mrp = mrp,
				SalePrice = salePrice,
				CatalogMedicineId = catalogMedicineId,
				IsActive = isActive,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_Drugs", x => x.Id);
		});
		migrationBuilder.CreateTable("LicenceRecords", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> licenceNumber = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> licenceType = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> issuingAuthority = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> issuedOn = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> expiresOn = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				LicenceNumber = licenceNumber,
				LicenceType = licenceType,
				IssuingAuthority = issuingAuthority,
				IssuedOn = issuedOn,
				ExpiresOn = expiresOn,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_LicenceRecords", x => x.Id);
		});
		migrationBuilder.CreateTable("NumberSeries", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> seriesPrefix = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> financialYear = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> lastNumber = table.Column<long>("INTEGER");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				SeriesPrefix = seriesPrefix,
				FinancialYear = financialYear,
				LastNumber = lastNumber,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_NumberSeries", x => x.Id);
		});
		migrationBuilder.CreateTable("Patients", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> name = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> phone = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> address = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> dateOfBirth = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> sex = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				Name = name,
				Phone = phone,
				Address = address,
				DateOfBirth = dateOfBirth,
				Sex = sex,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_Patients", x => x.Id);
		});
		migrationBuilder.CreateTable("PharmacyProfiles", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> name = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> legalName = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> address = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> phone = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> email = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> gstin = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> businessMode = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> currencyCode = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> timeZoneId = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				Name = name,
				LegalName = legalName,
				Address = address,
				Phone = phone,
				Email = email,
				Gstin = gstin,
				BusinessMode = businessMode,
				CurrencyCode = currencyCode,
				TimeZoneId = timeZoneId,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_PharmacyProfiles", x => x.Id);
		});
		migrationBuilder.CreateTable("Prescriptions", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> patientId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> prescriberName = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> prescriberRegistrationNumber = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> prescriptionDate = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> referenceNumber = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				PatientId = patientId,
				PrescriberName = prescriberName,
				PrescriberRegistrationNumber = prescriberRegistrationNumber,
				PrescriptionDate = prescriptionDate,
				ReferenceNumber = referenceNumber,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_Prescriptions", x => x.Id);
		});
		migrationBuilder.CreateTable("PurchaseInvoices", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> supplierId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> invoiceNo = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> invoiceDate = table.Column<DateOnly>("TEXT");
			OperationBuilder<AddColumnOperation> dueDate = table.Column<DateOnly>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> subtotal = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> taxAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> discountAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> totalAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> status = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				SupplierId = supplierId,
				InvoiceNo = invoiceNo,
				InvoiceDate = invoiceDate,
				DueDate = dueDate,
				Subtotal = subtotal,
				TaxAmount = taxAmount,
				DiscountAmount = discountAmount,
				TotalAmount = totalAmount,
				Status = status,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_PurchaseInvoices", x => x.Id);
		});
		migrationBuilder.CreateTable("PurchaseItems", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> purchaseInvoiceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> batchId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> quantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> freeQuantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> unitPrice = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> discountAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> taxRate = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> lineTotal = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				PurchaseInvoiceId = purchaseInvoiceId,
				DrugId = drugId,
				BatchId = batchId,
				Quantity = quantity,
				FreeQuantity = freeQuantity,
				UnitPrice = unitPrice,
				DiscountAmount = discountAmount,
				TaxRate = taxRate,
				LineTotal = lineTotal,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_PurchaseItems", x => x.Id);
		});
		migrationBuilder.CreateTable("Receipts", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> customerId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> supplierId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> receiptNo = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> receiptAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> amount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> paymentMethod = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> referenceNumber = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				CustomerId = customerId,
				SupplierId = supplierId,
				ReceiptNo = receiptNo,
				ReceiptAtUtc = receiptAtUtc,
				Amount = amount,
				PaymentMethod = paymentMethod,
				ReferenceNumber = referenceNumber,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_Receipts", x => x.Id);
		});
		migrationBuilder.CreateTable("ReturnNotes", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> returnNo = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> sourceType = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> sourceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> returnAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> totalAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> reason = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				ReturnNo = returnNo,
				SourceType = sourceType,
				SourceId = sourceId,
				ReturnAtUtc = returnAtUtc,
				TotalAmount = totalAmount,
				Reason = reason,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_ReturnNotes", x => x.Id);
		});
		migrationBuilder.CreateTable("SaleItems", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> saleId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> batchId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> quantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> unitPrice = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> discountAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> taxRate = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> lineTotal = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				SaleId = saleId,
				DrugId = drugId,
				BatchId = batchId,
				Quantity = quantity,
				UnitPrice = unitPrice,
				DiscountAmount = discountAmount,
				TaxRate = taxRate,
				LineTotal = lineTotal,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_SaleItems", x => x.Id);
		});
		migrationBuilder.CreateTable("Sales", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> patientId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> prescriptionId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> invoiceNo = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> saleAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> subtotal = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> taxAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> discountAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> totalAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> paidAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> paymentStatus = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				PatientId = patientId,
				PrescriptionId = prescriptionId,
				InvoiceNo = invoiceNo,
				SaleAtUtc = saleAtUtc,
				Subtotal = subtotal,
				TaxAmount = taxAmount,
				DiscountAmount = discountAmount,
				TotalAmount = totalAmount,
				PaidAmount = paidAmount,
				PaymentStatus = paymentStatus,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_Sales", x => x.Id);
		});
		migrationBuilder.CreateTable("ScheduleOverrides", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> schedule = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> reason = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> effectiveFromUtc = table.Column<DateTime>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> effectiveToUtc = table.Column<DateTime>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				DrugId = drugId,
				Schedule = schedule,
				Reason = reason,
				EffectiveFromUtc = effectiveFromUtc,
				EffectiveToUtc = effectiveToUtc,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_ScheduleOverrides", x => x.Id);
		});
		migrationBuilder.CreateTable("ScheduleRegisterEntries", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> registerType = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> saleId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> patientId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> entryAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> patientName = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> prescriberName = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> prescriberRegistrationNumber = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				RegisterType = registerType,
				SaleId = saleId,
				PatientId = patientId,
				DrugId = drugId,
				EntryAtUtc = entryAtUtc,
				PatientName = patientName,
				PrescriberName = prescriberName,
				PrescriberRegistrationNumber = prescriberRegistrationNumber,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_ScheduleRegisterEntries", x => x.Id);
		});
		migrationBuilder.CreateTable("StockMovements", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> batchId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> quantityChange = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> movementType = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> referenceType = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> referenceId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> movementAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				BatchId = batchId,
				DrugId = drugId,
				QuantityChange = quantityChange,
				MovementType = movementType,
				ReferenceType = referenceType,
				ReferenceId = referenceId,
				MovementAtUtc = movementAtUtc,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_StockMovements", x => x.Id);
		});
		migrationBuilder.CreateTable("SupplierLedgerEntries", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> supplierId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> entryAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> entryType = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> referenceId = table.Column<Guid>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> referenceNo = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> debit = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> credit = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				SupplierId = supplierId,
				EntryAtUtc = entryAtUtc,
				EntryType = entryType,
				ReferenceId = referenceId,
				ReferenceNo = referenceNo,
				Debit = debit,
				Credit = credit,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_SupplierLedgerEntries", x => x.Id);
		});
		migrationBuilder.CreateTable("Suppliers", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> name = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> phone = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> email = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> address = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> gstin = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> drugLicenceNumber = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> isActive = table.Column<bool>("INTEGER");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				Name = name,
				Phone = phone,
				Email = email,
				Address = address,
				Gstin = gstin,
				DrugLicenceNumber = drugLicenceNumber,
				IsActive = isActive,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_Suppliers", x => x.Id);
		});
		migrationBuilder.CreateTable("SyncConflicts", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> entityName = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> entityId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> localPayload = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> remotePayload = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> resolution = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> resolvedAtUtc = table.Column<DateTime>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				EntityName = entityName,
				EntityId = entityId,
				LocalPayload = localPayload,
				RemotePayload = remotePayload,
				Resolution = resolution,
				ResolvedAtUtc = resolvedAtUtc,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_SyncConflicts", x => x.Id);
		});
		migrationBuilder.CreateTable("WholesaleInvoiceItems", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> wholesaleInvoiceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> drugId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> batchId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> quantity = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> unitPrice = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> discountAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> taxRate = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> lineTotal = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				WholesaleInvoiceId = wholesaleInvoiceId,
				DrugId = drugId,
				BatchId = batchId,
				Quantity = quantity,
				UnitPrice = unitPrice,
				DiscountAmount = discountAmount,
				TaxRate = taxRate,
				LineTotal = lineTotal,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_WholesaleInvoiceItems", x => x.Id);
		});
		migrationBuilder.CreateTable("WholesaleInvoices", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> customerId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> invoiceNo = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> invoiceAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> subtotal = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> taxAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> discountAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> totalAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> paidAmount = table.Column<decimal>("decimal(18,2)");
			OperationBuilder<AddColumnOperation> paymentStatus = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> notes = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				CustomerId = customerId,
				InvoiceNo = invoiceNo,
				InvoiceAtUtc = invoiceAtUtc,
				Subtotal = subtotal,
				TaxAmount = taxAmount,
				DiscountAmount = discountAmount,
				TotalAmount = totalAmount,
				PaidAmount = paidAmount,
				PaymentStatus = paymentStatus,
				Notes = notes,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_WholesaleInvoices", x => x.Id);
		});
		migrationBuilder.CreateIndex("IX_AppUsers_Phone", "AppUsers", "Phone");
		migrationBuilder.CreateIndex("IX_AuditLogs_ActionAtUtc", "AuditLogs", "ActionAtUtc");
		migrationBuilder.CreateIndex("IX_Batches_BatchNo", "Batches", "BatchNo");
		migrationBuilder.CreateIndex("IX_Batches_ExpiryDate", "Batches", "ExpiryDate");
		migrationBuilder.CreateIndex("IX_CustomerLedgerEntries_EntryAtUtc", "CustomerLedgerEntries", "EntryAtUtc");
		migrationBuilder.CreateIndex("IX_Customers_Phone", "Customers", "Phone");
		migrationBuilder.CreateIndex("IX_Drugs_Name", "Drugs", "Name");
		migrationBuilder.CreateIndex("IX_LicenceRecords_ExpiresOn", "LicenceRecords", "ExpiresOn");
		migrationBuilder.CreateIndex("IX_NumberSeries_SeriesPrefix_FinancialYear", "NumberSeries", new string[2] { "SeriesPrefix", "FinancialYear" }, null, unique: true);
		migrationBuilder.CreateIndex("IX_Patients_Phone", "Patients", "Phone");
		migrationBuilder.CreateIndex("IX_PurchaseInvoices_InvoiceDate", "PurchaseInvoices", "InvoiceDate");
		migrationBuilder.CreateIndex("IX_PurchaseInvoices_InvoiceNo", "PurchaseInvoices", "InvoiceNo");
		migrationBuilder.CreateIndex("IX_Receipts_ReceiptAtUtc", "Receipts", "ReceiptAtUtc");
		migrationBuilder.CreateIndex("IX_Receipts_ReceiptNo", "Receipts", "ReceiptNo");
		migrationBuilder.CreateIndex("IX_ReturnNotes_ReturnAtUtc", "ReturnNotes", "ReturnAtUtc");
		migrationBuilder.CreateIndex("IX_ReturnNotes_ReturnNo", "ReturnNotes", "ReturnNo");
		migrationBuilder.CreateIndex("IX_Sales_InvoiceNo", "Sales", "InvoiceNo", null, unique: true);
		migrationBuilder.CreateIndex("IX_Sales_SaleAtUtc", "Sales", "SaleAtUtc");
		migrationBuilder.CreateIndex("IX_ScheduleRegisterEntries_EntryAtUtc", "ScheduleRegisterEntries", "EntryAtUtc");
		migrationBuilder.CreateIndex("IX_StockMovements_MovementAtUtc", "StockMovements", "MovementAtUtc");
		migrationBuilder.CreateIndex("IX_SupplierLedgerEntries_EntryAtUtc", "SupplierLedgerEntries", "EntryAtUtc");
		migrationBuilder.CreateIndex("IX_Suppliers_Phone", "Suppliers", "Phone");
		migrationBuilder.CreateIndex("IX_WholesaleInvoices_InvoiceAtUtc", "WholesaleInvoices", "InvoiceAtUtc");
		migrationBuilder.CreateIndex("IX_WholesaleInvoices_InvoiceNo", "WholesaleInvoices", "InvoiceNo", null, unique: true);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropTable("AppUsers");
		migrationBuilder.DropTable("AuditLogs");
		migrationBuilder.DropTable("Batches");
		migrationBuilder.DropTable("CatalogInfos");
		migrationBuilder.DropTable("CatalogMedicines");
		migrationBuilder.DropTable("ChangeLogs");
		migrationBuilder.DropTable("CustomerLedgerEntries");
		migrationBuilder.DropTable("CustomerLicences");
		migrationBuilder.DropTable("Customers");
		migrationBuilder.DropTable("DeviceInfos");
		migrationBuilder.DropTable("Drugs");
		migrationBuilder.DropTable("LicenceRecords");
		migrationBuilder.DropTable("NumberSeries");
		migrationBuilder.DropTable("Patients");
		migrationBuilder.DropTable("PharmacyProfiles");
		migrationBuilder.DropTable("Prescriptions");
		migrationBuilder.DropTable("PurchaseInvoices");
		migrationBuilder.DropTable("PurchaseItems");
		migrationBuilder.DropTable("Receipts");
		migrationBuilder.DropTable("ReturnNotes");
		migrationBuilder.DropTable("SaleItems");
		migrationBuilder.DropTable("Sales");
		migrationBuilder.DropTable("ScheduleOverrides");
		migrationBuilder.DropTable("ScheduleRegisterEntries");
		migrationBuilder.DropTable("StockMovements");
		migrationBuilder.DropTable("SupplierLedgerEntries");
		migrationBuilder.DropTable("Suppliers");
		migrationBuilder.DropTable("SyncConflicts");
		migrationBuilder.DropTable("WholesaleInvoiceItems");
		migrationBuilder.DropTable("WholesaleInvoices");
	}

	protected override void BuildTargetModel(ModelBuilder modelBuilder)
	{
		modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
		modelBuilder.Entity("PharmaBill.Core.Entities.AppUser", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("DisplayName").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Email").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsActive").HasColumnType("INTEGER");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<DateTime?>("LastLoginAtUtc").HasColumnType("TEXT");
			b.Property<string>("PasswordHash").HasColumnType("TEXT");
			b.Property<string>("Phone").HasColumnType("TEXT");
			b.Property<int>("Role").HasColumnType("INTEGER");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.Property<string>("UserName").IsRequired().HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("Phone");
			b.ToTable("AppUsers");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.AuditLog", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("Action").IsRequired().HasColumnType("TEXT");
			b.Property<DateTime>("ActionAtUtc").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<string>("Details").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<Guid?>("EntityId").HasColumnType("TEXT");
			b.Property<string>("EntityName").IsRequired().HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid?>("UserId").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("ActionAtUtc");
			b.ToTable("AuditLogs");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.Batch", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("BatchNo").IsRequired().HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<Guid>("DrugId").HasColumnType("TEXT");
			b.Property<DateOnly?>("ExpiryDate").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<decimal?>("Mrp").HasColumnType("decimal(18,2)");
			b.Property<decimal>("PurchasePrice").HasColumnType("decimal(18,2)");
			b.Property<decimal>("Quantity").HasColumnType("decimal(18,2)");
			b.Property<string>("Rack").HasColumnType("TEXT");
			b.Property<decimal?>("SalePrice").HasColumnType("decimal(18,2)");
			b.Property<Guid?>("SupplierId").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("BatchNo");
			b.HasIndex("ExpiryDate");
			b.ToTable("Batches");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.CatalogInfo", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<Guid?>("CatalogMedicineId").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<bool>("IsHabitForming").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<string>("RegisterType").HasColumnType("TEXT");
			b.Property<bool>("RequiresPrescription").HasColumnType("INTEGER");
			b.Property<string>("Schedule").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("CatalogInfos");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.CatalogMedicine", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("BrandName").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("DosageForm").HasColumnType("TEXT");
			b.Property<string>("GenericName").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Manufacturer").HasColumnType("TEXT");
			b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
			b.Property<string>("SourceId").HasColumnType("TEXT");
			b.Property<string>("Strength").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("CatalogMedicines");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.ChangeLog", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("ChangedAtUtc").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<Guid>("EntityId").HasColumnType("TEXT");
			b.Property<string>("EntityName").IsRequired().HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Operation").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Payload").IsRequired().HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("ChangeLogs");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.Customer", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("Address").HasColumnType("TEXT");
			b.Property<string>("BuyerType").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<decimal>("CreditLimit").HasColumnType("decimal(18,2)");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("Email").HasColumnType("TEXT");
			b.Property<string>("Gstin").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsActive").HasColumnType("INTEGER");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Phone").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("Phone");
			b.ToTable("Customers");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.CustomerLedgerEntry", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<decimal>("Credit").HasColumnType("decimal(18,2)");
			b.Property<Guid>("CustomerId").HasColumnType("TEXT");
			b.Property<decimal>("Debit").HasColumnType("decimal(18,2)");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<DateTime>("EntryAtUtc").HasColumnType("TEXT");
			b.Property<string>("EntryType").IsRequired().HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<Guid?>("ReferenceId").HasColumnType("TEXT");
			b.Property<string>("ReferenceNo").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("EntryAtUtc");
			b.ToTable("CustomerLedgerEntries");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.CustomerLicence", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("Authorisation").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("CustomerId").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<DateOnly?>("ExpiresOn").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("LicenceNumber").IsRequired().HasColumnType("TEXT");
			b.Property<string>("LicenceType").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("CustomerLicences");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.DeviceInfo", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("AppVersion").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("DeviceName").IsRequired().HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsCurrentDevice").HasColumnType("INTEGER");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<DateTime?>("LastSyncAtUtc").HasColumnType("TEXT");
			b.Property<string>("Platform").IsRequired().HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("DeviceInfos");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.Drug", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("Barcode").HasColumnType("TEXT");
			b.Property<string>("BrandName").HasColumnType("TEXT");
			b.Property<Guid?>("CatalogMedicineId").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("DosageForm").HasColumnType("TEXT");
			b.Property<string>("GenericName").HasColumnType("TEXT");
			b.Property<decimal?>("GstRate").HasColumnType("decimal(18,2)");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<string>("HsnCode").HasColumnType("TEXT");
			b.Property<bool>("IsActive").HasColumnType("INTEGER");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<decimal?>("Mrp").HasColumnType("decimal(18,2)");
			b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
			b.Property<decimal?>("SalePrice").HasColumnType("decimal(18,2)");
			b.Property<string>("Schedule").HasColumnType("TEXT");
			b.Property<string>("Strength").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<string>("Unit").HasColumnType("TEXT");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("Name");
			b.ToTable("Drugs");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.LicenceRecord", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<DateOnly?>("ExpiresOn").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<DateOnly?>("IssuedOn").HasColumnType("TEXT");
			b.Property<string>("IssuingAuthority").HasColumnType("TEXT");
			b.Property<string>("LicenceNumber").IsRequired().HasColumnType("TEXT");
			b.Property<string>("LicenceType").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("ExpiresOn");
			b.ToTable("LicenceRecords");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.NumberSeries", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("FinancialYear").IsRequired().HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<long>("LastNumber").HasColumnType("INTEGER");
			b.Property<string>("SeriesPrefix").IsRequired().HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("SeriesPrefix", "FinancialYear").IsUnique();
			b.ToTable("NumberSeries");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.Patient", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("Address").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<DateOnly?>("DateOfBirth").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Phone").HasColumnType("TEXT");
			b.Property<string>("Sex").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("Phone");
			b.ToTable("Patients");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.PharmacyProfile", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("Address").HasColumnType("TEXT");
			b.Property<string>("BusinessMode").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<string>("CurrencyCode").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("Email").HasColumnType("TEXT");
			b.Property<string>("Gstin").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("LegalName").HasColumnType("TEXT");
			b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Phone").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<string>("TimeZoneId").HasColumnType("TEXT");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("PharmacyProfiles");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.Prescription", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<Guid?>("PatientId").HasColumnType("TEXT");
			b.Property<string>("PrescriberName").HasColumnType("TEXT");
			b.Property<string>("PrescriberRegistrationNumber").HasColumnType("TEXT");
			b.Property<DateOnly?>("PrescriptionDate").HasColumnType("TEXT");
			b.Property<string>("ReferenceNumber").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("Prescriptions");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.PurchaseInvoice", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<decimal>("DiscountAmount").HasColumnType("decimal(18,2)");
			b.Property<DateOnly?>("DueDate").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<DateOnly>("InvoiceDate").HasColumnType("TEXT");
			b.Property<string>("InvoiceNo").IsRequired().HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<string>("Status").HasColumnType("TEXT");
			b.Property<decimal>("Subtotal").HasColumnType("decimal(18,2)");
			b.Property<Guid>("SupplierId").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<decimal>("TaxAmount").HasColumnType("decimal(18,2)");
			b.Property<decimal>("TotalAmount").HasColumnType("decimal(18,2)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("InvoiceDate");
			b.HasIndex("InvoiceNo");
			b.ToTable("PurchaseInvoices");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.PurchaseItem", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<Guid?>("BatchId").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<decimal>("DiscountAmount").HasColumnType("decimal(18,2)");
			b.Property<Guid>("DrugId").HasColumnType("TEXT");
			b.Property<decimal>("FreeQuantity").HasColumnType("decimal(18,2)");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<decimal>("LineTotal").HasColumnType("decimal(18,2)");
			b.Property<Guid>("PurchaseInvoiceId").HasColumnType("TEXT");
			b.Property<decimal>("Quantity").HasColumnType("decimal(18,2)");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<decimal>("TaxRate").HasColumnType("decimal(18,2)");
			b.Property<decimal>("UnitPrice").HasColumnType("decimal(18,2)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("PurchaseItems");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.Receipt", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<decimal>("Amount").HasColumnType("decimal(18,2)");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid?>("CustomerId").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<string>("PaymentMethod").IsRequired().HasColumnType("TEXT");
			b.Property<DateTime>("ReceiptAtUtc").HasColumnType("TEXT");
			b.Property<string>("ReceiptNo").IsRequired().HasColumnType("TEXT");
			b.Property<string>("ReferenceNumber").HasColumnType("TEXT");
			b.Property<Guid?>("SupplierId").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("ReceiptAtUtc");
			b.HasIndex("ReceiptNo");
			b.ToTable("Receipts");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.ReturnNote", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<string>("Reason").IsRequired().HasColumnType("TEXT");
			b.Property<DateTime>("ReturnAtUtc").HasColumnType("TEXT");
			b.Property<string>("ReturnNo").IsRequired().HasColumnType("TEXT");
			b.Property<Guid>("SourceId").HasColumnType("TEXT");
			b.Property<string>("SourceType").IsRequired().HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<decimal>("TotalAmount").HasColumnType("decimal(18,2)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("ReturnAtUtc");
			b.HasIndex("ReturnNo");
			b.ToTable("ReturnNotes");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.Sale", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<decimal>("DiscountAmount").HasColumnType("decimal(18,2)");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<string>("InvoiceNo").IsRequired().HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<decimal>("PaidAmount").HasColumnType("decimal(18,2)");
			b.Property<Guid?>("PatientId").HasColumnType("TEXT");
			b.Property<string>("PaymentStatus").HasColumnType("TEXT");
			b.Property<Guid?>("PrescriptionId").HasColumnType("TEXT");
			b.Property<DateTime>("SaleAtUtc").HasColumnType("TEXT");
			b.Property<decimal>("Subtotal").HasColumnType("decimal(18,2)");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<decimal>("TaxAmount").HasColumnType("decimal(18,2)");
			b.Property<decimal>("TotalAmount").HasColumnType("decimal(18,2)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("InvoiceNo").IsUnique();
			b.HasIndex("SaleAtUtc");
			b.ToTable("Sales");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.SaleItem", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<Guid>("BatchId").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<decimal>("DiscountAmount").HasColumnType("decimal(18,2)");
			b.Property<Guid>("DrugId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<decimal>("LineTotal").HasColumnType("decimal(18,2)");
			b.Property<decimal>("Quantity").HasColumnType("decimal(18,2)");
			b.Property<Guid>("SaleId").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<decimal>("TaxRate").HasColumnType("decimal(18,2)");
			b.Property<decimal>("UnitPrice").HasColumnType("decimal(18,2)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("SaleItems");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.ScheduleOverride", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<Guid>("DrugId").HasColumnType("TEXT");
			b.Property<DateTime?>("EffectiveFromUtc").HasColumnType("TEXT");
			b.Property<DateTime?>("EffectiveToUtc").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Reason").HasColumnType("TEXT");
			b.Property<string>("Schedule").IsRequired().HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("ScheduleOverrides");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.ScheduleRegisterEntry", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<Guid?>("DrugId").HasColumnType("TEXT");
			b.Property<DateTime>("EntryAtUtc").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<Guid?>("PatientId").HasColumnType("TEXT");
			b.Property<string>("PatientName").HasColumnType("TEXT");
			b.Property<string>("PrescriberName").HasColumnType("TEXT");
			b.Property<string>("PrescriberRegistrationNumber").HasColumnType("TEXT");
			b.Property<string>("RegisterType").IsRequired().HasColumnType("TEXT");
			b.Property<Guid?>("SaleId").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("EntryAtUtc");
			b.ToTable("ScheduleRegisterEntries");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.StockMovement", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<Guid>("BatchId").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<Guid>("DrugId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<DateTime>("MovementAtUtc").HasColumnType("TEXT");
			b.Property<string>("MovementType").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<decimal>("QuantityChange").HasColumnType("decimal(18,2)");
			b.Property<Guid?>("ReferenceId").HasColumnType("TEXT");
			b.Property<string>("ReferenceType").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("MovementAtUtc");
			b.ToTable("StockMovements");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.Supplier", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("Address").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("DrugLicenceNumber").HasColumnType("TEXT");
			b.Property<string>("Email").HasColumnType("TEXT");
			b.Property<string>("Gstin").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsActive").HasColumnType("INTEGER");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Phone").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("Phone");
			b.ToTable("Suppliers");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.SupplierLedgerEntry", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<decimal>("Credit").HasColumnType("decimal(18,2)");
			b.Property<decimal>("Debit").HasColumnType("decimal(18,2)");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<DateTime>("EntryAtUtc").HasColumnType("TEXT");
			b.Property<string>("EntryType").IsRequired().HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<Guid?>("ReferenceId").HasColumnType("TEXT");
			b.Property<string>("ReferenceNo").HasColumnType("TEXT");
			b.Property<Guid>("SupplierId").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("EntryAtUtc");
			b.ToTable("SupplierLedgerEntries");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.SyncConflict", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<Guid>("EntityId").HasColumnType("TEXT");
			b.Property<string>("EntityName").IsRequired().HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("LocalPayload").IsRequired().HasColumnType("TEXT");
			b.Property<string>("RemotePayload").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Resolution").HasColumnType("TEXT");
			b.Property<DateTime?>("ResolvedAtUtc").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("SyncConflicts");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.WholesaleInvoice", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("CustomerId").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<decimal>("DiscountAmount").HasColumnType("decimal(18,2)");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<DateTime>("InvoiceAtUtc").HasColumnType("TEXT");
			b.Property<string>("InvoiceNo").IsRequired().HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<decimal>("PaidAmount").HasColumnType("decimal(18,2)");
			b.Property<string>("PaymentStatus").HasColumnType("TEXT");
			b.Property<decimal>("Subtotal").HasColumnType("decimal(18,2)");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<decimal>("TaxAmount").HasColumnType("decimal(18,2)");
			b.Property<decimal>("TotalAmount").HasColumnType("decimal(18,2)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("InvoiceAtUtc");
			b.HasIndex("InvoiceNo").IsUnique();
			b.ToTable("WholesaleInvoices");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.WholesaleInvoiceItem", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<Guid>("BatchId").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<decimal>("DiscountAmount").HasColumnType("decimal(18,2)");
			b.Property<Guid>("DrugId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<decimal>("LineTotal").HasColumnType("decimal(18,2)");
			b.Property<decimal>("Quantity").HasColumnType("decimal(18,2)");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<decimal>("TaxRate").HasColumnType("decimal(18,2)");
			b.Property<decimal>("UnitPrice").HasColumnType("decimal(18,2)");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("WholesaleInvoiceId").HasColumnType("TEXT");
			b.HasKey("Id");
			b.ToTable("WholesaleInvoiceItems");
		});
	}
}
