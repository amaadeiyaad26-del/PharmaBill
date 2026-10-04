using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class BackupAndReportsTests
{
    [Fact]
    public async Task EncryptedBackup_RestoreRestoresRowCountsAndKeyTableChecksums()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PharmaBill.BackupTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var keyProvider = new DatabaseEncryptionKeyProvider(Path.Combine(root, "database.key"));
        keyProvider.ReplaceKey(key);
        var databasePath = Path.Combine(root, "pharmabill.db");
        var context = CreateContext(databasePath, key);
        try
        {
            await context.Database.MigrateAsync();
            Directory.CreateDirectory(Path.Combine(root, "prescriptions"));
            await File.WriteAllTextAsync(Path.Combine(root, "prescriptions", "rx-scan.pdf"), "sample attachment");
            Directory.CreateDirectory(Path.Combine(root, "licences"));
            await File.WriteAllTextAsync(Path.Combine(root, "licences", "licence-copy.pdf"), "licence attachment");
            var drug = new Drug { Name = "Backup checksum medicine", HsnCode = "3004" };
            var batch = new Batch { DrugId = drug.Id, BatchNo = "BK-1", Quantity = 7m, PurchasePrice = 12m };
            var customer = new Customer { Name = "Backup customer" };
            context.AddRange(drug, batch, customer);
            context.CustomerLicences.Add(new CustomerLicence
            {
                CustomerId = customer.Id,
                LicenceNumber = "BK-LIC-1",
                LicenceType = "W1",
                DocumentPath = Path.Combine(root, "licences", "licence-copy.pdf")
            });
            context.Prescriptions.Add(new Prescription
            {
                Notes = $"DocumentPath={Path.Combine(root, "prescriptions", "rx-scan.pdf")}"
            });
            context.StockMovements.Add(new StockMovement
            {
                DrugId = drug.Id,
                BatchId = batch.Id,
                MovementType = "PurchaseReceipt",
                QuantityChange = 7m
            });
            await context.SaveChangesAsync();

            var service = new EncryptedBackupService(
                context,
                new DatabaseStorageOptions(root),
                keyProvider);
            var backupPath = Path.Combine(root, "roundtrip.pbbak");
            var originalManifest = await service.CreateBackupAsync(backupPath, "a-long-test-password");
            var backupBytes = await File.ReadAllBytesAsync(backupPath);
            Assert.False(backupBytes.AsSpan().StartsWith("PK"u8));

            context.Drugs.Add(new Drug { Name = "Must disappear after restore" });
            await context.SaveChangesAsync();
            var result = await service.RestoreBackupAsync(backupPath, "a-long-test-password");
            Assert.True(File.Exists(result.SafetyBackupPath));
            Assert.Contains("Drugs", result.Manifest.TableRowCounts.Keys);
            Assert.Contains("Drugs", result.Manifest.KeyTableChecksums.Keys);
            Assert.Equal("sample attachment", await File.ReadAllTextAsync(Path.Combine(root, "prescriptions", "rx-scan.pdf")));
            Assert.Equal("licence attachment", await File.ReadAllTextAsync(Path.Combine(root, "licences", "licence-copy.pdf")));

            await context.DisposeAsync();
            await using var restored = CreateContext(databasePath, key);
            Assert.Equal(1, await restored.Drugs.CountAsync());
            Assert.Equal("Backup checksum medicine", (await restored.Drugs.SingleAsync()).Name);
            Assert.Equal(1, await restored.StockMovements.CountAsync());
            Assert.Equal(7m, await restored.StockMovements.SumAsync(item => item.QuantityChange));
            Assert.Equal(
                Path.Combine(root, "licences", "licence-copy.pdf"),
                (await restored.CustomerLicences.SingleAsync()).DocumentPath);
            Assert.Equal(
                $"DocumentPath={Path.Combine(root, "prescriptions", "rx-scan.pdf")}",
                (await restored.Prescriptions.SingleAsync()).Notes);
            var restoredManifest = await new EncryptedBackupService(
                restored,
                new DatabaseStorageOptions(root),
                keyProvider).CreateBackupAsync(Path.Combine(root, "after-restore.pbbak"), "a-long-test-password");
            Assert.Equal(originalManifest.TableRowCounts, restoredManifest.TableRowCounts);
            Assert.Equal(originalManifest.KeyTableChecksums, restoredManifest.KeyTableChecksums);
        }
        finally
        {
            await context.DisposeAsync();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task EncryptedBackup_WrongPasswordDoesNotModifyCurrentDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), $"PharmaBill.BackupTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var keyProvider = new DatabaseEncryptionKeyProvider(Path.Combine(root, "database.key"));
        keyProvider.ReplaceKey(key);
        var context = CreateContext(Path.Combine(root, "pharmabill.db"), key);
        try
        {
            await context.Database.MigrateAsync();
            context.Drugs.Add(new Drug { Name = "Protected current data" });
            await context.SaveChangesAsync();
            var service = new EncryptedBackupService(context, new DatabaseStorageOptions(root), keyProvider);
            var backupPath = Path.Combine(root, "wrong-password.pbbak");
            await service.CreateBackupAsync(backupPath, "another-long-password");

            await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() =>
                service.RestoreBackupAsync(backupPath, "incorrect-password"));
            Assert.Equal(1, await context.Drugs.CountAsync());
        }
        finally
        {
            await context.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsAndDashboard_UseSelectedPeriodAndCurrentMovementBalances()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var now = DateTime.Now;
        var drug = new Drug { Name = "Report medicine", HsnCode = "3004", GstRate = 12m, ReorderLevel = 5m };
        var patient = new Patient { Name = "Report patient", Phone = "9876543210" };
        var supplier = new Supplier { Name = "Report supplier" };
        var customer = new Customer { Name = "Report wholesale buyer" };
        var batch = new Batch
        {
            DrugId = drug.Id,
            BatchNo = "R-1",
            ExpiryDate = DateOnly.FromDateTime(now.Date.AddDays(20)),
            PurchasePrice = 4m,
            Mrp = 10m
        };
        var sale = new Sale
        {
            PatientId = patient.Id,
            InvoiceNo = $"RPT-{Guid.NewGuid():N}",
            SaleAtUtc = now.ToUniversalTime(),
            TotalAmount = 112m,
            TaxAmount = 12m,
            PaidAmount = 112m
        };
        var purchase = new PurchaseInvoice
        {
            SupplierId = supplier.Id,
            InvoiceNo = "PUR-REPORT-1",
            InvoiceDate = DateOnly.FromDateTime(now),
            Subtotal = 20m,
            TaxAmount = 2.4m,
            TotalAmount = 22.4m,
            Status = "Posted"
        };
        var wholesaleInvoice = new WholesaleInvoice
        {
            CustomerId = customer.Id,
            InvoiceNo = "WHO-REPORT-1",
            InvoiceAtUtc = now.ToUniversalTime(),
            TotalAmount = 50m,
            PaidAmount = 10m,
            Status = "Posted"
        };
        database.Context.AddRange(drug, patient, supplier, customer, batch, sale, purchase, wholesaleInvoice);
        database.Context.PurchaseItems.Add(new PurchaseItem
        {
            PurchaseInvoiceId = purchase.Id,
            DrugId = drug.Id,
            BatchId = batch.Id,
            Quantity = 5m,
            UnitPrice = 4m,
            TaxRate = 12m,
            LineTotal = 22.4m
        });
        database.Context.SaleItems.Add(new SaleItem
        {
            SaleId = sale.Id,
            DrugId = drug.Id,
            BatchId = batch.Id,
            Quantity = 10m,
            UnitPrice = 11.2m,
            TaxRate = 12m,
            LineTotal = 112m
        });
        database.Context.StockMovements.Add(new StockMovement
        {
            DrugId = drug.Id,
            BatchId = batch.Id,
            MovementAtUtc = now.ToUniversalTime(),
            MovementType = "PurchaseReceipt",
            QuantityChange = 3m
        });
        database.Context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
        {
            RegisterType = "H1",
            EntryAtUtc = now.ToUniversalTime(),
            Notes = "Test alert"
        });
        await database.Context.SaveChangesAsync();

        var reports = new ReportsDashboardService(database.Context);
        var from = DateOnly.FromDateTime(now);
        var salesReport = await reports.GetReportAsync(ReportKind.Sales, from, from);
        var purchasesReport = await reports.GetReportAsync(ReportKind.Purchases, from, from);
        var supplierWise = await reports.GetReportAsync(ReportKind.SupplierWise, from, from);
        var profitReport = await reports.GetReportAsync(ReportKind.Profit, from, from);
        var gstReport = await reports.GetReportAsync(ReportKind.GstSummary, from, from);
        var topSelling = await reports.GetReportAsync(ReportKind.TopSellingDrugs, from, from);
        var lowStock = await reports.GetReportAsync(ReportKind.LowStock, from, from);
        var expiry = await reports.GetReportAsync(ReportKind.Expiry, from, from.AddDays(90));
        var dashboard = await reports.GetDashboardAsync(now);

        Assert.Equal(2, salesReport.Rows.Count);
        Assert.Equal("Report supplier", Assert.Single(purchasesReport.Rows)[2]);
        Assert.Equal("Report supplier", Assert.Single(supplierWise.Rows)[0]);
        Assert.Equal("Report medicine", Assert.Single(profitReport.Rows)[0]);
        Assert.Equal("3004", Assert.Single(gstReport.Rows)[0]);
        Assert.Equal("Report medicine", Assert.Single(topSelling.Rows)[0]);
        Assert.Single(lowStock.Rows);
        Assert.Single(expiry.Rows);
        Assert.Equal(162m, dashboard.TodaySales);
        Assert.Equal(162m, dashboard.MonthSales);
        Assert.Equal(1, dashboard.ExpiringSoonBatches);
        Assert.Equal(1, dashboard.LowStockDrugs);
        Assert.Equal(40m, dashboard.WholesaleOutstanding);
        Assert.Equal(1, dashboard.RegisterAlerts);
    }

    private static PharmaBillDbContext CreateContext(string databasePath, byte[] key)
    {
        SQLitePCL.Batteries_V2.Init();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Password = Convert.ToHexString(key),
            Pooling = false
        }.ToString();
        var options = new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SaveChangesAuditInterceptor(new DatabaseDeviceId(Guid.NewGuid())))
            .Options;
        return new PharmaBillDbContext(options, new DatabaseDeviceId(Guid.NewGuid()));
    }
}
