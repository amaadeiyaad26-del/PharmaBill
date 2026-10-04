using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Persistence;

public sealed class PharmaBillDbContext : DbContext
{
    public PharmaBillDbContext(
        DbContextOptions<PharmaBillDbContext> options,
        DatabaseDeviceId deviceId)
        : base(options) => DeviceId = deviceId.Value;

    public Guid DeviceId { get; }

    public bool IsApplyingSync { get; set; }

    public DbSet<PharmacyProfile> PharmacyProfiles => Set<PharmacyProfile>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Ignore<EntityBase>();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToArray())
        {
            var clrType = entityType.ClrType;
            if (!typeof(EntityBase).IsAssignableFrom(clrType))
            {
                continue;
            }

            var entity = modelBuilder.Entity(clrType);
            entity.HasKey(nameof(EntityBase.Id));
            entity.Property(nameof(EntityBase.Id)).ValueGeneratedNever();
            entity.Property(nameof(EntityBase.HlcStamp)).HasMaxLength(80).IsRequired();
            entity.Property(nameof(EntityBase.SyncState)).HasConversion<int>();
            entity.HasQueryFilter(CreateSoftDeleteFilter(clrType));

            foreach (var property in entityType.GetProperties()
                         .Where(property => property.ClrType == typeof(decimal) ||
                                            property.ClrType == typeof(decimal?)))
            {
                entity.Property(property.Name).HasColumnType("decimal(18,2)");
            }
        }

        modelBuilder.Entity<Drug>().HasIndex(item => item.Name);
        modelBuilder.Entity<CatalogMedicine>().HasIndex(item => item.SourceId).IsUnique();
        modelBuilder.Entity<CatalogMedicine>().HasIndex(item => item.CompositionKey);
        modelBuilder.Entity<CatalogInfo>().HasIndex(item => item.NameKey).IsUnique();
        modelBuilder.Entity<CatalogImportState>().HasIndex(item => item.ImportKey).IsUnique();
        modelBuilder.Entity<Batch>().HasIndex(item => item.BatchNo);
        modelBuilder.Entity<Batch>().HasIndex(item => item.ExpiryDate);
        modelBuilder.Entity<DrugPackLevel>().HasIndex(item => new { item.DrugId, item.Level }).IsUnique();
        modelBuilder.Entity<CustomerCategoryPrice>().HasIndex(item => new { item.DrugId, item.PriceCategory, item.EffectiveFrom });
        modelBuilder.Entity<TradeDiscount>().HasIndex(item => new { item.PriceCategory, item.EffectiveFrom });
        modelBuilder.Entity<WholesaleScheme>().HasIndex(item => new { item.DrugId, item.PriceCategory, item.StartsOn, item.EndsOn });
        modelBuilder.Entity<WholesaleRateHistory>().HasIndex(item => new { item.CustomerId, item.DrugId, item.SoldAtUtc });
        modelBuilder.Entity<LicenceRecord>().HasIndex(item => item.ExpiresOn);
        modelBuilder.Entity<LicenceRecord>().HasIndex(item => new { item.LicenceType, item.ExpiresOn });
        modelBuilder.Entity<Patient>().HasIndex(item => item.Phone);
        modelBuilder.Entity<Customer>().HasIndex(item => item.Phone);
        modelBuilder.Entity<Customer>().HasIndex(item => item.Gstin);
        modelBuilder.Entity<Supplier>().HasIndex(item => item.Phone);
        modelBuilder.Entity<AppUser>().HasIndex(item => item.Phone);
        modelBuilder.Entity<AppUser>().HasIndex(item => item.UserName).IsUnique();
        modelBuilder.Entity<Sale>().HasIndex(item => item.InvoiceNo).IsUnique();
        modelBuilder.Entity<Sale>().HasIndex(item => item.SaleAtUtc);
        modelBuilder.Entity<PurchaseInvoice>().HasIndex(item => new { item.SupplierId, item.InvoiceNo }).IsUnique();
        modelBuilder.Entity<PurchaseInvoice>().HasIndex(item => item.InvoiceDate);
        modelBuilder.Entity<PurchaseReturn>().HasIndex(item => item.ReturnNo).IsUnique();
        modelBuilder.Entity<PurchaseReturn>().HasIndex(item => item.ReturnDate);
        modelBuilder.Entity<StockVerificationSession>().HasIndex(item => item.SessionNo).IsUnique();
        modelBuilder.Entity<ExpiryWriteOff>().HasIndex(item => item.ExpiryDate);
        modelBuilder.Entity<WholesaleInvoice>().HasIndex(item => item.InvoiceNo).IsUnique();
        modelBuilder.Entity<WholesaleInvoice>().HasIndex(item => item.InvoiceAtUtc);
        modelBuilder.Entity<CustomerLicence>().HasIndex(item => item.LicenceNumber);
        modelBuilder.Entity<CustomerLicence>().HasIndex(item => new { item.CustomerId, item.ExpiresOn });
        modelBuilder.Entity<Receipt>().HasIndex(item => item.ReceiptNo);
        modelBuilder.Entity<Receipt>().HasIndex(item => item.ReceiptAtUtc);
        modelBuilder.Entity<Receipt>().HasIndex(item => item.WholesaleInvoiceId);
        modelBuilder.Entity<WholesaleInvoice>().HasIndex(item => item.Status);
        modelBuilder.Entity<StockMovement>().HasIndex(item => item.MovementAtUtc);
        modelBuilder.Entity<CustomerLedgerEntry>().HasIndex(item => item.EntryAtUtc);
        modelBuilder.Entity<SupplierLedgerEntry>().HasIndex(item => item.EntryAtUtc);
        modelBuilder.Entity<ReturnNote>().HasIndex(item => item.ReturnNo);
        modelBuilder.Entity<ReturnNote>().HasIndex(item => item.ReturnAtUtc);
        modelBuilder.Entity<SaleReturnItem>().HasIndex(item => item.ReturnNoteId);
        modelBuilder.Entity<SaleReturnItem>().HasIndex(item => item.SaleItemId);
        modelBuilder.Entity<WholesaleReturnItem>().HasIndex(item => item.WholesaleInvoiceItemId);
        modelBuilder.Entity<WholesaleReturnItem>().HasIndex(item => item.ReturnNoteId);
        modelBuilder.Entity<ScheduleRegisterEntry>().HasIndex(item => item.EntryAtUtc);
        modelBuilder.Entity<AuditLog>().HasIndex(item => item.ActionAtUtc);
        modelBuilder.Entity<NumberSeries>()
            .HasIndex(item => new { item.SeriesPrefix, item.FinancialYear })
            .IsUnique();
    }

    private static LambdaExpression CreateSoftDeleteFilter(Type entityType)
    {
        var parameter = Expression.Parameter(entityType, "entity");
        var isDeleted = Expression.Property(parameter, nameof(EntityBase.IsDeleted));
        return Expression.Lambda(Expression.Equal(isDeleted, Expression.Constant(false)), parameter);
    }
}
