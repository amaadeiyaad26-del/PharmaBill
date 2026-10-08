using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Persistence;

public sealed class PharmaBillDbContext : DbContext
{
	public Guid DeviceId { get; }

	public bool IsApplyingSync { get; set; }

	public DbSet<PharmacyProfile> PharmacyProfiles => Set<PharmacyProfile>();

	public DbSet<Branch> Branches => Set<Branch>();

	public DbSet<LicenceRecord> LicenceRecords => Set<LicenceRecord>();

	public DbSet<AppUser> AppUsers => Set<AppUser>();

	public DbSet<Drug> Drugs => Set<Drug>();

	public DbSet<CatalogMedicine> CatalogMedicines => Set<CatalogMedicine>();

	public DbSet<CatalogInfo> CatalogInfos => Set<CatalogInfo>();

	public DbSet<CatalogImportState> CatalogImportStates => Set<CatalogImportState>();

	public DbSet<ScheduleOverride> ScheduleOverrides => Set<ScheduleOverride>();

	public DbSet<Supplier> Suppliers => Set<Supplier>();

	public DbSet<Batch> Batches => Set<Batch>();

	public DbSet<DrugPackLevel> DrugPackLevels => Set<DrugPackLevel>();

	public DbSet<CustomerCategoryPrice> CustomerCategoryPrices => Set<CustomerCategoryPrice>();

	public DbSet<TradeDiscount> TradeDiscounts => Set<TradeDiscount>();

	public DbSet<WholesaleScheme> WholesaleSchemes => Set<WholesaleScheme>();

	public DbSet<WholesaleRateHistory> WholesaleRateHistory => Set<WholesaleRateHistory>();

	public DbSet<StockMovement> StockMovements => Set<StockMovement>();

	public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

	public DbSet<StockLocationBalance> StockLocationBalances => Set<StockLocationBalance>();

	public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();

	public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();

	public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();

	public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();

	public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();

	public DbSet<PurchaseReturnItem> PurchaseReturnItems => Set<PurchaseReturnItem>();

	public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();

	public DbSet<StockVerificationSession> StockVerificationSessions => Set<StockVerificationSession>();

	public DbSet<StockVerificationItem> StockVerificationItems => Set<StockVerificationItem>();

	public DbSet<ExpiryWriteOff> ExpiryWriteOffs => Set<ExpiryWriteOff>();

	public DbSet<Patient> Patients => Set<Patient>();

	public DbSet<Prescription> Prescriptions => Set<Prescription>();

	public DbSet<Sale> Sales => Set<Sale>();

	public DbSet<SaleItem> SaleItems => Set<SaleItem>();

	public DbSet<Customer> Customers => Set<Customer>();

	public DbSet<CustomerLicence> CustomerLicences => Set<CustomerLicence>();

	public DbSet<WholesaleInvoice> WholesaleInvoices => Set<WholesaleInvoice>();

	public DbSet<WholesaleInvoiceItem> WholesaleInvoiceItems => Set<WholesaleInvoiceItem>();

	public DbSet<Receipt> Receipts => Set<Receipt>();

	public DbSet<CustomerLedgerEntry> CustomerLedgerEntries => Set<CustomerLedgerEntry>();

	public DbSet<SupplierLedgerEntry> SupplierLedgerEntries => Set<SupplierLedgerEntry>();

	public DbSet<ReturnNote> ReturnNotes => Set<ReturnNote>();

	public DbSet<SaleReturnItem> SaleReturnItems => Set<SaleReturnItem>();

	public DbSet<WholesaleReturnItem> WholesaleReturnItems => Set<WholesaleReturnItem>();

	public DbSet<ScheduleRegisterEntry> ScheduleRegisterEntries => Set<ScheduleRegisterEntry>();

	public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

	public DbSet<ChangeLog> ChangeLogs => Set<ChangeLog>();

	public DbSet<SyncConflict> SyncConflicts => Set<SyncConflict>();

	public DbSet<DeviceInfo> DeviceInfos => Set<DeviceInfo>();

	public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();

	public PharmaBillDbContext(DbContextOptions<PharmaBillDbContext> options, DatabaseDeviceId deviceId)
		: base(options)
	{
		DeviceId = deviceId.Value;
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		base.OnModelCreating(modelBuilder);
		modelBuilder.Ignore<EntityBase>();
		IMutableEntityType[] array = modelBuilder.Model.GetEntityTypes().ToArray();
		foreach (IMutableEntityType mutableEntityType in array)
		{
			Type clrType = mutableEntityType.ClrType;
			if (!typeof(EntityBase).IsAssignableFrom(clrType))
			{
				continue;
			}
			EntityTypeBuilder entityTypeBuilder = modelBuilder.Entity(clrType);
			entityTypeBuilder.HasKey("Id");
			entityTypeBuilder.Property("Id").ValueGeneratedNever();
			entityTypeBuilder.Property("HlcStamp").HasMaxLength(80).IsRequired();
			entityTypeBuilder.Property("SyncState").HasConversion<int>();
			entityTypeBuilder.HasQueryFilter(CreateSoftDeleteFilter(clrType));
			foreach (IMutableProperty item in from property in mutableEntityType.GetProperties()
				where property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?)
				select property)
			{
				entityTypeBuilder.Property(item.Name).HasColumnType("decimal(18,2)");
			}
		}
		modelBuilder.Entity<Drug>().HasIndex((Drug item) => item.Name);
		modelBuilder.Entity<CatalogMedicine>().HasIndex((CatalogMedicine item) => item.SourceId).IsUnique();
		modelBuilder.Entity<CatalogMedicine>().HasIndex((CatalogMedicine item) => item.CompositionKey);
		modelBuilder.Entity<CatalogInfo>().HasIndex((CatalogInfo item) => item.NameKey).IsUnique();
		modelBuilder.Entity<CatalogImportState>().HasIndex((CatalogImportState item) => item.ImportKey).IsUnique();
		modelBuilder.Entity<Batch>().HasIndex((Batch item) => item.BatchNo);
		modelBuilder.Entity<Batch>().HasIndex((Batch item) => item.ExpiryDate);
		modelBuilder.Entity<DrugPackLevel>().HasIndex((DrugPackLevel item) => new { item.DrugId, item.Level }).IsUnique();
		modelBuilder.Entity<CustomerCategoryPrice>().HasIndex((CustomerCategoryPrice item) => new { item.DrugId, item.PriceCategory, item.EffectiveFrom });
		modelBuilder.Entity<TradeDiscount>().HasIndex((TradeDiscount item) => new { item.PriceCategory, item.EffectiveFrom });
		modelBuilder.Entity<WholesaleScheme>().HasIndex((WholesaleScheme item) => new { item.DrugId, item.PriceCategory, item.StartsOn, item.EndsOn });
		modelBuilder.Entity<WholesaleRateHistory>().HasIndex((WholesaleRateHistory item) => new { item.CustomerId, item.DrugId, item.SoldAtUtc });
		modelBuilder.Entity<LicenceRecord>().HasIndex((LicenceRecord item) => item.ExpiresOn);
		modelBuilder.Entity<LicenceRecord>().HasIndex((LicenceRecord item) => new { item.LicenceType, item.ExpiresOn });
		modelBuilder.Entity<Patient>().HasIndex((Patient item) => item.Phone);
		modelBuilder.Entity<Customer>().HasIndex((Customer item) => item.Phone);
		modelBuilder.Entity<Customer>().HasIndex((Customer item) => item.Gstin);
		modelBuilder.Entity<Supplier>().HasIndex((Supplier item) => item.Phone);
		modelBuilder.Entity<AppUser>().HasIndex((AppUser item) => item.Phone);
		modelBuilder.Entity<AppUser>().HasIndex((AppUser item) => item.UserName).IsUnique();
		modelBuilder.Entity<AppUser>().HasIndex((AppUser item) => item.Email);
		modelBuilder.Entity<AppUser>().HasIndex((AppUser item) => new { item.AuthProvider, item.ProviderSubjectId });
		modelBuilder.Entity<Branch>().HasIndex((Branch item) => item.Code).IsUnique();
		modelBuilder.Entity<Sale>().HasIndex((Sale item) => item.InvoiceNo).IsUnique();
		modelBuilder.Entity<Sale>().HasIndex((Sale item) => item.BranchId);
		modelBuilder.Entity<PurchaseInvoice>().HasIndex((PurchaseInvoice item) => item.BranchId);
		modelBuilder.Entity<Batch>().HasIndex((Batch item) => item.BranchId);
		modelBuilder.Entity<AuditLog>().HasIndex((AuditLog item) => item.BranchId);
		modelBuilder.Entity<Sale>().HasIndex((Sale item) => item.SaleAtUtc).IsDescending()
			.HasDatabaseName("idx_sales_date");
		modelBuilder.Entity<PurchaseInvoice>().HasIndex((PurchaseInvoice item) => new { item.SupplierId, item.InvoiceNo }).IsUnique();
		modelBuilder.Entity<PurchaseInvoice>().HasIndex((PurchaseInvoice item) => item.InvoiceDate).IsDescending()
			.HasDatabaseName("idx_purchases_date");
		modelBuilder.Entity<PurchaseReturn>().HasIndex((PurchaseReturn item) => item.ReturnNo).IsUnique();
		modelBuilder.Entity<PurchaseReturn>().HasIndex((PurchaseReturn item) => item.ReturnDate);
		modelBuilder.Entity<StockVerificationSession>().HasIndex((StockVerificationSession item) => item.SessionNo).IsUnique();
		modelBuilder.Entity<ExpiryWriteOff>().HasIndex((ExpiryWriteOff item) => item.ExpiryDate);
		modelBuilder.Entity<WholesaleInvoice>().HasIndex((WholesaleInvoice item) => item.InvoiceNo).IsUnique();
		modelBuilder.Entity<WholesaleInvoice>().HasIndex((WholesaleInvoice item) => item.InvoiceAtUtc).IsDescending()
			.HasDatabaseName("idx_wholesale_invoices_date");
		modelBuilder.Entity<CustomerLicence>().HasIndex((CustomerLicence item) => item.LicenceNumber);
		modelBuilder.Entity<CustomerLicence>().HasIndex((CustomerLicence item) => new { item.CustomerId, item.ExpiresOn });
		modelBuilder.Entity<Receipt>().HasIndex((Receipt item) => item.ReceiptNo);
		modelBuilder.Entity<Receipt>().HasIndex((Receipt item) => item.ReceiptAtUtc);
		modelBuilder.Entity<Receipt>().HasIndex((Receipt item) => item.WholesaleInvoiceId);
		modelBuilder.Entity<WholesaleInvoice>().HasIndex((WholesaleInvoice item) => item.Status);
		modelBuilder.Entity<StockMovement>().HasIndex((StockMovement item) => item.MovementAtUtc).IsDescending()
			.HasDatabaseName("idx_stock_movements_date");
		modelBuilder.Entity<StockMovement>().HasIndex((StockMovement item) => new { item.LocationId, item.BatchId });
		modelBuilder.Entity<StorageLocation>().HasIndex((StorageLocation item) => item.Code).IsUnique();
		modelBuilder.Entity<StockLocationBalance>().HasIndex((StockLocationBalance item) => new { item.LocationId, item.BatchId }).IsUnique();
		modelBuilder.Entity<StockTransfer>().HasIndex((StockTransfer item) => item.TransferNumber).IsUnique();
		modelBuilder.Entity<StockTransfer>().HasIndex((StockTransfer item) => item.TransferDate);
		modelBuilder.Entity<StockTransferItem>().HasIndex((StockTransferItem item) => item.StockTransferId);
		modelBuilder.Entity<CustomerLedgerEntry>().HasIndex((CustomerLedgerEntry item) => item.EntryAtUtc);
		modelBuilder.Entity<SupplierLedgerEntry>().HasIndex((SupplierLedgerEntry item) => item.EntryAtUtc);
		modelBuilder.Entity<ReturnNote>().HasIndex((ReturnNote item) => item.ReturnNo);
		modelBuilder.Entity<ReturnNote>().HasIndex((ReturnNote item) => item.ReturnAtUtc);
		modelBuilder.Entity<SaleReturnItem>().HasIndex((SaleReturnItem item) => item.ReturnNoteId);
		modelBuilder.Entity<SaleReturnItem>().HasIndex((SaleReturnItem item) => item.SaleItemId);
		modelBuilder.Entity<WholesaleReturnItem>().HasIndex((WholesaleReturnItem item) => item.WholesaleInvoiceItemId);
		modelBuilder.Entity<WholesaleReturnItem>().HasIndex((WholesaleReturnItem item) => item.ReturnNoteId);
		modelBuilder.Entity<ScheduleRegisterEntry>().HasIndex((ScheduleRegisterEntry item) => item.EntryAtUtc);
		modelBuilder.Entity<AuditLog>().HasIndex((AuditLog item) => item.ActionAtUtc);
		modelBuilder.Entity<NumberSeries>().HasIndex((NumberSeries item) => new { item.SeriesPrefix, item.FinancialYear }).IsUnique();
	}

	public override int SaveChanges()
	{
		RejectAuditLogDeletion();
		return base.SaveChanges();
	}

	public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		RejectAuditLogDeletion();
		return base.SaveChangesAsync(cancellationToken);
	}

	private void RejectAuditLogDeletion()
	{
		foreach (var entry in ChangeTracker.Entries<AuditLog>())
		{
			if (entry.State == EntityState.Deleted || (entry.State == EntityState.Modified && entry.Entity.IsDeleted))
			{
				throw new InvalidOperationException("Audit log rows cannot be deleted.");
			}
		}
	}

	private static LambdaExpression CreateSoftDeleteFilter(Type entityType)
	{
		ParameterExpression parameterExpression = Expression.Parameter(entityType, "entity");
		return Expression.Lambda(Expression.Equal(Expression.Property(parameterExpression, "IsDeleted"), Expression.Constant(false)), parameterExpression);
	}
}
