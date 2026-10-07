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
[Migration("20261003134040_MedicineCatalogImport")]
public class MedicineCatalogImport : Migration
{
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.Sql("CREATE VIRTUAL TABLE CatalogMedicineFts USING fts5(\r\n    MedicineId UNINDEXED,\r\n    Name,\r\n    Composition,\r\n    Manufacturer,\r\n    tokenize='unicode61 remove_diacritics 2'\r\n);");
		migrationBuilder.AddColumn<string>("CompositionKey", "CatalogMedicines", "TEXT", null, null, rowVersion: false, null, nullable: false, "");
		migrationBuilder.AddColumn<bool>("IsDiscontinued", "CatalogMedicines", "INTEGER", null, null, rowVersion: false, null, nullable: false, false);
		migrationBuilder.AddColumn<string>("PackSizeLabel", "CatalogMedicines", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<decimal>("ReferencePrice", "CatalogMedicines", "decimal(18,2)", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("ShortComposition1", "CatalogMedicines", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("ShortComposition2", "CatalogMedicines", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("Type", "CatalogMedicines", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("ActionClass", "CatalogInfos", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("ChemicalClass", "CatalogInfos", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("NameKey", "CatalogInfos", "TEXT", null, null, rowVersion: false, null, nullable: false, "");
		migrationBuilder.AddColumn<string>("TherapeuticClass", "CatalogInfos", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.AddColumn<string>("Use", "CatalogInfos", "TEXT", null, null, rowVersion: false, null, nullable: true);
		migrationBuilder.CreateTable("CatalogImportStates", (ColumnsBuilder table) =>
		{
			OperationBuilder<AddColumnOperation> id = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> importKey = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> filePath = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> fileFingerprint = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> lastProcessedRecord = table.Column<long>("INTEGER");
			OperationBuilder<AddColumnOperation> importedRows = table.Column<long>("INTEGER");
			OperationBuilder<AddColumnOperation> skippedRows = table.Column<long>("INTEGER");
			OperationBuilder<AddColumnOperation> status = table.Column<string>("TEXT");
			OperationBuilder<AddColumnOperation> lastError = table.Column<string>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> completedAtUtc = table.Column<DateTime>("TEXT", null, null, rowVersion: false, null, nullable: true);
			OperationBuilder<AddColumnOperation> createdAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> updatedAtUtc = table.Column<DateTime>("TEXT");
			OperationBuilder<AddColumnOperation> deviceId = table.Column<Guid>("TEXT");
			OperationBuilder<AddColumnOperation> isDeleted = table.Column<bool>("INTEGER");
			int? maxLength = 80;
			return new
			{
				Id = id,
				ImportKey = importKey,
				FilePath = filePath,
				FileFingerprint = fileFingerprint,
				LastProcessedRecord = lastProcessedRecord,
				ImportedRows = importedRows,
				SkippedRows = skippedRows,
				Status = status,
				LastError = lastError,
				CompletedAtUtc = completedAtUtc,
				CreatedAtUtc = createdAtUtc,
				UpdatedAtUtc = updatedAtUtc,
				DeviceId = deviceId,
				IsDeleted = isDeleted,
				HlcStamp = table.Column<string>("TEXT", null, maxLength),
				SyncState = table.Column<int>("INTEGER")
			};
		}, null, table =>
		{
			table.PrimaryKey("PK_CatalogImportStates", x => x.Id);
		});
		migrationBuilder.CreateIndex("IX_CatalogMedicines_CompositionKey", "CatalogMedicines", "CompositionKey");
		migrationBuilder.CreateIndex("IX_CatalogMedicines_SourceId", "CatalogMedicines", "SourceId", null, unique: true);
		migrationBuilder.CreateIndex("IX_CatalogInfos_NameKey", "CatalogInfos", "NameKey", null, unique: true);
		migrationBuilder.CreateIndex("IX_CatalogImportStates_ImportKey", "CatalogImportStates", "ImportKey", null, unique: true);
	}

	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.Sql("DROP TABLE IF EXISTS CatalogMedicineFts;");
		migrationBuilder.DropTable("CatalogImportStates");
		migrationBuilder.DropIndex("IX_CatalogMedicines_CompositionKey", "CatalogMedicines");
		migrationBuilder.DropIndex("IX_CatalogMedicines_SourceId", "CatalogMedicines");
		migrationBuilder.DropIndex("IX_CatalogInfos_NameKey", "CatalogInfos");
		migrationBuilder.DropColumn("CompositionKey", "CatalogMedicines");
		migrationBuilder.DropColumn("IsDiscontinued", "CatalogMedicines");
		migrationBuilder.DropColumn("PackSizeLabel", "CatalogMedicines");
		migrationBuilder.DropColumn("ReferencePrice", "CatalogMedicines");
		migrationBuilder.DropColumn("ShortComposition1", "CatalogMedicines");
		migrationBuilder.DropColumn("ShortComposition2", "CatalogMedicines");
		migrationBuilder.DropColumn("Type", "CatalogMedicines");
		migrationBuilder.DropColumn("ActionClass", "CatalogInfos");
		migrationBuilder.DropColumn("ChemicalClass", "CatalogInfos");
		migrationBuilder.DropColumn("NameKey", "CatalogInfos");
		migrationBuilder.DropColumn("TherapeuticClass", "CatalogInfos");
		migrationBuilder.DropColumn("Use", "CatalogInfos");
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
			b.Property<int>("IdleLockMinutes").HasColumnType("INTEGER");
			b.Property<bool>("IsActive").HasColumnType("INTEGER");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<DateTime?>("LastLoginAtUtc").HasColumnType("TEXT");
			b.Property<string>("PasswordHash").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Phone").HasColumnType("TEXT");
			b.Property<int>("Role").HasColumnType("INTEGER");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.Property<bool>("UseWindowsHello").HasColumnType("INTEGER");
			b.Property<string>("UserName").IsRequired().HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("Phone");
			b.HasIndex("UserName").IsUnique();
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
		modelBuilder.Entity("PharmaBill.Core.Entities.CatalogImportState", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<DateTime?>("CompletedAtUtc").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("FileFingerprint").IsRequired().HasColumnType("TEXT");
			b.Property<string>("FilePath").IsRequired().HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<string>("ImportKey").IsRequired().HasColumnType("TEXT");
			b.Property<long>("ImportedRows").HasColumnType("INTEGER");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<string>("LastError").HasColumnType("TEXT");
			b.Property<long>("LastProcessedRecord").HasColumnType("INTEGER");
			b.Property<long>("SkippedRows").HasColumnType("INTEGER");
			b.Property<string>("Status").IsRequired().HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("ImportKey").IsUnique();
			b.ToTable("CatalogImportStates");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.CatalogInfo", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("ActionClass").HasColumnType("TEXT");
			b.Property<Guid?>("CatalogMedicineId").HasColumnType("TEXT");
			b.Property<string>("ChemicalClass").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<bool>("IsHabitForming").HasColumnType("INTEGER");
			b.Property<string>("NameKey").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Notes").HasColumnType("TEXT");
			b.Property<string>("RegisterType").HasColumnType("TEXT");
			b.Property<bool>("RequiresPrescription").HasColumnType("INTEGER");
			b.Property<string>("Schedule").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<string>("TherapeuticClass").HasColumnType("TEXT");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.Property<string>("Use").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("NameKey").IsUnique();
			b.ToTable("CatalogInfos");
		});
		modelBuilder.Entity("PharmaBill.Core.Entities.CatalogMedicine", (EntityTypeBuilder b) =>
		{
			b.Property<Guid>("Id").HasColumnType("TEXT");
			b.Property<string>("BrandName").HasColumnType("TEXT");
			b.Property<string>("CompositionKey").IsRequired().HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("DosageForm").HasColumnType("TEXT");
			b.Property<string>("GenericName").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<bool>("IsDiscontinued").HasColumnType("INTEGER");
			b.Property<string>("Manufacturer").HasColumnType("TEXT");
			b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
			b.Property<string>("PackSizeLabel").HasColumnType("TEXT");
			b.Property<decimal?>("ReferencePrice").HasColumnType("decimal(18,2)");
			b.Property<string>("ShortComposition1").HasColumnType("TEXT");
			b.Property<string>("ShortComposition2").HasColumnType("TEXT");
			b.Property<string>("SourceId").HasColumnType("TEXT");
			b.Property<string>("Strength").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<string>("Type").HasColumnType("TEXT");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.HasKey("Id");
			b.HasIndex("CompositionKey");
			b.HasIndex("SourceId").IsUnique();
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
			b.Property<string>("DocumentPath").HasColumnType("TEXT");
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
			b.HasIndex("LicenceType", "ExpiresOn");
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
			b.Property<string>("BankAccountName").HasColumnType("TEXT");
			b.Property<string>("BankAccountNumber").HasColumnType("TEXT");
			b.Property<string>("BankIfsc").HasColumnType("TEXT");
			b.Property<string>("BankName").HasColumnType("TEXT");
			b.Property<int>("BusinessMode").HasColumnType("INTEGER");
			b.Property<bool>("ClockRollbackDetected").HasColumnType("INTEGER");
			b.Property<string>("CompetentPersonName").HasColumnType("TEXT");
			b.Property<string>("CompetentPersonQualification").HasColumnType("TEXT");
			b.Property<string>("CompetentPersonRegistrationNumber").HasColumnType("TEXT");
			b.Property<DateTime>("CreatedAtUtc").HasColumnType("TEXT");
			b.Property<string>("CurrencyCode").HasColumnType("TEXT");
			b.Property<Guid>("DeviceId").HasColumnType("TEXT");
			b.Property<string>("Email").HasColumnType("TEXT");
			b.Property<string>("Gstin").HasColumnType("TEXT");
			b.Property<string>("HlcStamp").IsRequired().HasMaxLength(80)
				.HasColumnType("TEXT");
			b.Property<string>("InvoicePrefix").IsRequired().HasColumnType("TEXT");
			b.Property<bool>("IsDeleted").HasColumnType("INTEGER");
			b.Property<DateTime?>("LastEntitlementCheckAtUtc").HasColumnType("TEXT");
			b.Property<string>("LegalName").HasColumnType("TEXT");
			b.Property<string>("Name").IsRequired().HasColumnType("TEXT");
			b.Property<string>("Pan").HasColumnType("TEXT");
			b.Property<string>("Phone").HasColumnType("TEXT");
			b.Property<int>("SyncState").HasColumnType("INTEGER");
			b.Property<string>("TimeZoneId").HasColumnType("TEXT");
			b.Property<DateTime?>("TrialStartedAtUtc").HasColumnType("TEXT");
			b.Property<DateTime>("UpdatedAtUtc").HasColumnType("TEXT");
			b.Property<string>("UpiId").HasColumnType("TEXT");
			b.Property<string>("WholesaleLicenceTypesJson").IsRequired().HasColumnType("TEXT");
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
