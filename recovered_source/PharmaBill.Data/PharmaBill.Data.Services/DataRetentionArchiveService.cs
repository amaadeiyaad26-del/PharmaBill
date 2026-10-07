using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class DataRetentionArchiveService(PharmaBillDbContext context, DatabaseStorageOptions storage, DatabaseEncryptionKeyProvider keyProvider, DataRetentionSettingsStore settingsStore)
{
	private sealed record ExportBundle(long TotalRows, Dictionary<string, long> TableCounts, Dictionary<string, List<Guid>> SoftDeleteIds, IReadOnlyList<string> SoftDeleteTables, IReadOnlyList<string> ExportOnlyTables);

	private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PHARMAR1");

	private const int Pbkdf2Iterations = 600000;

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	public DataRetentionPeriod GetConfiguredPeriod()
	{
		return settingsStore.Load().Period;
	}

	public void SavePeriod(DataRetentionPeriod period)
	{
		DataRetentionSettings dataRetentionSettings = settingsStore.Load();
		settingsStore.Save(dataRetentionSettings with
		{
			Period = period
		});
	}

	public async Task<DataRetentionActiveSummary> GetActiveSummaryAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		DataRetentionPeriod period = GetConfiguredPeriod();
		DateTime cutoff = StatutoryRetentionPolicy.GetCutoffUtc(period);
		List<DateTime> oldestCandidates = new List<DateTime>();
		int salesCount = await context.Sales.CountAsync(cancellationToken);
		if (salesCount > 0)
		{
			List<DateTime> list = oldestCandidates;
			list.Add(await context.Sales.MinAsync((Sale item) => item.SaleAtUtc, cancellationToken));
		}
		int wholesaleCount = await context.WholesaleInvoices.CountAsync(cancellationToken);
		if (wholesaleCount > 0)
		{
			List<DateTime> list = oldestCandidates;
			list.Add(await context.WholesaleInvoices.MinAsync((WholesaleInvoice item) => item.InvoiceAtUtc, cancellationToken));
		}
		int purchaseCount = await context.PurchaseInvoices.CountAsync(cancellationToken);
		if (purchaseCount > 0)
		{
			oldestCandidates.Add((await context.PurchaseInvoices.MinAsync((PurchaseInvoice item) => item.InvoiceDate, cancellationToken)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
		}
		int purchaseReturnCount = await context.PurchaseReturns.CountAsync(cancellationToken);
		if (purchaseReturnCount > 0)
		{
			oldestCandidates.Add((await context.PurchaseReturns.MinAsync((PurchaseReturn item) => item.ReturnDate, cancellationToken)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
		}
		int returnCount = await context.ReturnNotes.CountAsync(cancellationToken);
		if (returnCount > 0)
		{
			List<DateTime> list = oldestCandidates;
			list.Add(await context.ReturnNotes.MinAsync((ReturnNote item) => item.ReturnAtUtc, cancellationToken));
		}
		int adjustmentCount = await context.StockAdjustments.CountAsync(cancellationToken);
		if (adjustmentCount > 0)
		{
			List<DateTime> list = oldestCandidates;
			list.Add(await context.StockAdjustments.MinAsync((StockAdjustment item) => item.AdjustedAtUtc, cancellationToken));
		}
		int writeOffCount = await context.ExpiryWriteOffs.CountAsync(cancellationToken);
		if (writeOffCount > 0)
		{
			List<DateTime> list = oldestCandidates;
			list.Add(await context.ExpiryWriteOffs.MinAsync((ExpiryWriteOff item) => item.WrittenOffAtUtc, cancellationToken));
		}
		int registerCount = await context.ScheduleRegisterEntries.CountAsync(cancellationToken);
		if (registerCount > 0)
		{
			List<DateTime> list = oldestCandidates;
			list.Add(await context.ScheduleRegisterEntries.MinAsync((ScheduleRegisterEntry item) => item.EntryAtUtc, cancellationToken));
		}
		int movementCount = await context.StockMovements.CountAsync(cancellationToken);
		if (movementCount > 0)
		{
			List<DateTime> list = oldestCandidates;
			list.Add(await context.StockMovements.MinAsync((StockMovement item) => item.MovementAtUtc, cancellationToken));
		}
		int activeCount = salesCount + wholesaleCount + purchaseCount + purchaseReturnCount + returnCount + adjustmentCount + writeOffCount + registerCount + movementCount;
		DateTime? oldest = ((oldestCandidates.Count == 0) ? ((DateTime?)null) : new DateTime?(oldestCandidates.Min()));
		long num = ((period != DataRetentionPeriod.Permanent) ? (await CountArchivableRowsAsync(cutoff, cancellationToken)) : 0);
		long candidateArchiveCount = num;
		return new DataRetentionActiveSummary(activeCount, oldest, cutoff, period, candidateArchiveCount);
	}

	public async Task<DataRetentionArchiveResult> ArchiveAndPruneAsync(string? archivePassword = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		DataRetentionSettings settings = settingsStore.Load();
		if (settings.Period == DataRetentionPeriod.Permanent)
		{
			throw new InvalidOperationException("Retention is set to Permanent / Do Not Purge. Change the period before archiving.");
		}
		DateTime cutoff = StatutoryRetentionPolicy.GetCutoffUtc(settings.Period);
		string password = (string.IsNullOrWhiteSpace(archivePassword) ? Convert.ToHexString(keyProvider.GetOrCreateKey()) : archivePassword);
		if (password.Length < 10)
		{
			throw new InvalidOperationException("Archive password must be at least 10 characters.");
		}
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"PharmaBill.Archive.{Guid.NewGuid():N}");
		Directory.CreateDirectory(workingDirectory);
		string archiveDbPath = Path.Combine(workingDirectory, "archive.db");
		string zipPath = Path.Combine(workingDirectory, "archive.zip");
		Directory.CreateDirectory(storage.ArchivesPath);
		int year = cutoff.Year;
		string destinationPath = Path.Combine(storage.ArchivesPath, $"pharmabill_archive_{year}.zip");
		if (File.Exists(destinationPath))
		{
			destinationPath = Path.Combine(storage.ArchivesPath, $"pharmabill_archive_{year}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
		}
		try
		{
			ExportBundle export = await ExportArchivableRowsAsync(archiveDbPath, cutoff, cancellationToken);
			if (export.TotalRows == 0L)
			{
				throw new InvalidOperationException($"No transactional records older than {cutoff:yyyy-MM-dd} UTC were found to archive.");
			}
			var value = new
			{
				Format = "PharmaBill encrypted retention archive",
				ArchiveVersion = 1,
				CreatedAtUtc = DateTime.UtcNow,
				CutoffUtc = cutoff,
				RetentionPeriod = settings.Period.ToString(),
				RetentionYears = StatutoryRetentionPolicy.GetRetentionYears(settings.Period),
				TableCounts = export.TableCounts,
				ExportedRowCount = export.TotalRows,
				SoftDeleteTables = export.SoftDeleteTables,
				ExportOnlyTables = export.ExportOnlyTables,
				Notes = "Patients, medicines, batches and customers are not archived or deleted. Stock movements and statutory register entries are exported only (not soft-deleted) so foreign keys and stock recomputation remain intact."
			};
			string manifestPath = Path.Combine(workingDirectory, "manifest.json");
			await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(value, JsonOptions), cancellationToken);
			await using (ZipArchive destination = ZipFile.Open(zipPath, ZipArchiveMode.Create))
			{
				destination.CreateEntryFromFile(archiveDbPath, "archive.db", CompressionLevel.Optimal);
				destination.CreateEntryFromFile(manifestPath, "manifest.json", CompressionLevel.Optimal);
			}
			await EncryptArchiveAsync(zipPath, destinationPath, password, cancellationToken);
			long softDeleted = await SoftDeleteExportedDocumentsAsync(cutoff, export.SoftDeleteIds, cancellationToken);
			context.AuditLogs.Add(new AuditLog
			{
				ActionAtUtc = DateTime.UtcNow,
				Action = "DataRetentionArchive",
				EntityName = "RetentionArchive",
				Details = $"Archived {export.TotalRows} row(s) before {cutoff:O}; soft-deleted {softDeleted}; file={destinationPath}"
			});
			await context.SaveChangesAsync(cancellationToken);
			settingsStore.Save(settings with
			{
				LastArchiveAtUtc = DateTime.UtcNow,
				LastArchivePath = destinationPath,
				LastArchivedRowCount = export.TotalRows
			});
			return new DataRetentionArchiveResult(destinationPath, cutoff, export.TotalRows, softDeleted, export.TableCounts);
		}
		finally
		{
			TryDeleteDirectory(workingDirectory);
		}
	}

	private async Task<long> CountArchivableRowsAsync(DateTime cutoff, CancellationToken cancellationToken)
	{
		DateOnly cutoffDate = DateOnly.FromDateTime(cutoff);
		long num = 0L;
		long num2 = num;
		num = num2 + await context.Sales.CountAsync((Sale item) => item.SaleAtUtc < cutoff, cancellationToken);
		num2 = num;
		num = num2 + await context.WholesaleInvoices.CountAsync((WholesaleInvoice item) => item.InvoiceAtUtc < cutoff, cancellationToken);
		num2 = num;
		num = num2 + await context.PurchaseInvoices.CountAsync((PurchaseInvoice item) => item.InvoiceDate < cutoffDate, cancellationToken);
		num2 = num;
		num = num2 + await context.PurchaseReturns.CountAsync((PurchaseReturn item) => item.ReturnDate < cutoffDate, cancellationToken);
		num2 = num;
		num = num2 + await context.ReturnNotes.CountAsync((ReturnNote item) => item.ReturnAtUtc < cutoff, cancellationToken);
		num2 = num;
		num = num2 + await context.StockAdjustments.CountAsync((StockAdjustment item) => item.AdjustedAtUtc < cutoff, cancellationToken);
		num2 = num;
		num = num2 + await context.ExpiryWriteOffs.CountAsync((ExpiryWriteOff item) => item.WrittenOffAtUtc < cutoff, cancellationToken);
		num2 = num;
		num = num2 + await context.Receipts.CountAsync((Receipt item) => item.ReceiptAtUtc < cutoff, cancellationToken);
		num2 = num;
		num = num2 + await context.CustomerLedgerEntries.CountAsync((CustomerLedgerEntry item) => item.EntryAtUtc < cutoff, cancellationToken);
		num2 = num;
		num = num2 + await context.SupplierLedgerEntries.CountAsync((SupplierLedgerEntry item) => item.EntryAtUtc < cutoff, cancellationToken);
		num2 = num;
		num = num2 + await context.ScheduleRegisterEntries.CountAsync((ScheduleRegisterEntry item) => item.EntryAtUtc < cutoff, cancellationToken);
		num2 = num;
		return num2 + await context.StockMovements.CountAsync((StockMovement item) => item.MovementAtUtc < cutoff, cancellationToken);
	}

	private async Task<ExportBundle> ExportArchivableRowsAsync(string archiveDbPath, DateTime cutoff, CancellationToken cancellationToken)
	{
		DateOnly cutoffDate = DateOnly.FromDateTime(cutoff);
		Dictionary<string, long> tableCounts = new Dictionary<string, long>(StringComparer.Ordinal);
		Dictionary<string, List<Guid>> softDeleteIds = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
		long total = 0L;
		SqliteConnection connection = new SqliteConnection(new SqliteConnectionStringBuilder
		{
			DataSource = archiveDbPath,
			Mode = SqliteOpenMode.ReadWriteCreate,
			Pooling = false
		}.ToString());
		ExportBundle result;
		try
		{
			await connection.OpenAsync(cancellationToken);
			await using (SqliteCommand create = connection.CreateCommand())
			{
				create.CommandText = "CREATE TABLE ArchivedRows (\r\n    TableName TEXT NOT NULL,\r\n    Id TEXT NOT NULL,\r\n    BusinessDate TEXT NOT NULL,\r\n    PayloadJson TEXT NOT NULL,\r\n    SoftDelete INTEGER NOT NULL,\r\n    PRIMARY KEY (TableName, Id)\r\n);\r\nCREATE INDEX IX_ArchivedRows_BusinessDate ON ArchivedRows(BusinessDate);";
				await create.ExecuteNonQueryAsync(cancellationToken);
			}
			List<Sale> sales = await (from item in context.Sales.AsNoTracking()
				where item.SaleAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			long num = total;
			total = num + await WriteAsync<Sale>("Sales", sales, (Sale item) => item.Id, (Sale item) => item.SaleAtUtc, softDelete: true);
			if (sales.Count > 0)
			{
				HashSet<Guid> saleIds = sales.Select((Sale item) => item.Id).ToHashSet();
				List<SaleItem> rows = await (from item in context.SaleItems.AsNoTracking()
					where saleIds.Contains(item.SaleId)
					select item).ToListAsync(cancellationToken);
				num = total;
				total = num + await WriteAsync<SaleItem>("SaleItems", rows, (SaleItem item) => item.Id, (SaleItem item) => item.CreatedAtUtc, softDelete: true);
			}
			List<WholesaleInvoice> wholesale = await (from item in context.WholesaleInvoices.AsNoTracking()
				where item.InvoiceAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<WholesaleInvoice>("WholesaleInvoices", wholesale, (WholesaleInvoice item) => item.Id, (WholesaleInvoice item) => item.InvoiceAtUtc, softDelete: true);
			if (wholesale.Count > 0)
			{
				HashSet<Guid> invoiceIds = wholesale.Select((WholesaleInvoice item) => item.Id).ToHashSet();
				List<WholesaleInvoiceItem> rows2 = await (from item in context.WholesaleInvoiceItems.AsNoTracking()
					where invoiceIds.Contains(item.WholesaleInvoiceId)
					select item).ToListAsync(cancellationToken);
				num = total;
				total = num + await WriteAsync<WholesaleInvoiceItem>("WholesaleInvoiceItems", rows2, (WholesaleInvoiceItem item) => item.Id, (WholesaleInvoiceItem item) => item.CreatedAtUtc, softDelete: true);
			}
			List<PurchaseInvoice> purchases = await (from item in context.PurchaseInvoices.AsNoTracking()
				where item.InvoiceDate < cutoffDate
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<PurchaseInvoice>("PurchaseInvoices", purchases, (PurchaseInvoice item) => item.Id, (PurchaseInvoice item) => item.InvoiceDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), softDelete: true);
			if (purchases.Count > 0)
			{
				HashSet<Guid> purchaseIds = purchases.Select((PurchaseInvoice item) => item.Id).ToHashSet();
				List<PurchaseItem> rows3 = await (from item in context.PurchaseItems.AsNoTracking()
					where purchaseIds.Contains(item.PurchaseInvoiceId)
					select item).ToListAsync(cancellationToken);
				num = total;
				total = num + await WriteAsync<PurchaseItem>("PurchaseItems", rows3, (PurchaseItem item) => item.Id, (PurchaseItem item) => item.CreatedAtUtc, softDelete: true);
			}
			List<PurchaseReturn> purchaseReturns = await (from item in context.PurchaseReturns.AsNoTracking()
				where item.ReturnDate < cutoffDate
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<PurchaseReturn>("PurchaseReturns", purchaseReturns, (PurchaseReturn item) => item.Id, (PurchaseReturn item) => item.ReturnDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), softDelete: true);
			if (purchaseReturns.Count > 0)
			{
				HashSet<Guid> returnIds = purchaseReturns.Select((PurchaseReturn item) => item.Id).ToHashSet();
				List<PurchaseReturnItem> rows4 = await (from item in context.PurchaseReturnItems.AsNoTracking()
					where returnIds.Contains(item.PurchaseReturnId)
					select item).ToListAsync(cancellationToken);
				num = total;
				total = num + await WriteAsync<PurchaseReturnItem>("PurchaseReturnItems", rows4, (PurchaseReturnItem item) => item.Id, (PurchaseReturnItem item) => item.CreatedAtUtc, softDelete: true);
			}
			List<ReturnNote> returnNotes = await (from item in context.ReturnNotes.AsNoTracking()
				where item.ReturnAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<ReturnNote>("ReturnNotes", returnNotes, (ReturnNote item) => item.Id, (ReturnNote item) => item.ReturnAtUtc, softDelete: true);
			if (returnNotes.Count > 0)
			{
				HashSet<Guid> noteIds = returnNotes.Select((ReturnNote item) => item.Id).ToHashSet();
				List<SaleReturnItem> rows5 = await (from item in context.SaleReturnItems.AsNoTracking()
					where noteIds.Contains(item.ReturnNoteId)
					select item).ToListAsync(cancellationToken);
				num = total;
				total = num + await WriteAsync<SaleReturnItem>("SaleReturnItems", rows5, (SaleReturnItem item) => item.Id, (SaleReturnItem item) => item.CreatedAtUtc, softDelete: true);
				List<WholesaleReturnItem> rows6 = await (from item in context.WholesaleReturnItems.AsNoTracking()
					where noteIds.Contains(item.ReturnNoteId)
					select item).ToListAsync(cancellationToken);
				num = total;
				total = num + await WriteAsync<WholesaleReturnItem>("WholesaleReturnItems", rows6, (WholesaleReturnItem item) => item.Id, (WholesaleReturnItem item) => item.CreatedAtUtc, softDelete: true);
			}
			List<StockAdjustment> rows7 = await (from item in context.StockAdjustments.AsNoTracking()
				where item.AdjustedAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<StockAdjustment>("StockAdjustments", rows7, (StockAdjustment item) => item.Id, (StockAdjustment item) => item.AdjustedAtUtc, softDelete: true);
			List<ExpiryWriteOff> rows8 = await (from item in context.ExpiryWriteOffs.AsNoTracking()
				where item.WrittenOffAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<ExpiryWriteOff>("ExpiryWriteOffs", rows8, (ExpiryWriteOff item) => item.Id, (ExpiryWriteOff item) => item.WrittenOffAtUtc, softDelete: true);
			List<Receipt> rows9 = await (from item in context.Receipts.AsNoTracking()
				where item.ReceiptAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<Receipt>("Receipts", rows9, (Receipt item) => item.Id, (Receipt item) => item.ReceiptAtUtc, softDelete: true);
			List<CustomerLedgerEntry> rows10 = await (from item in context.CustomerLedgerEntries.AsNoTracking()
				where item.EntryAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<CustomerLedgerEntry>("CustomerLedgerEntries", rows10, (CustomerLedgerEntry item) => item.Id, (CustomerLedgerEntry item) => item.EntryAtUtc, softDelete: true);
			List<SupplierLedgerEntry> rows11 = await (from item in context.SupplierLedgerEntries.AsNoTracking()
				where item.EntryAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<SupplierLedgerEntry>("SupplierLedgerEntries", rows11, (SupplierLedgerEntry item) => item.Id, (SupplierLedgerEntry item) => item.EntryAtUtc, softDelete: true);
			List<StockMovement> rows12 = await (from item in context.StockMovements.AsNoTracking()
				where item.MovementAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<StockMovement>("StockMovements", rows12, (StockMovement item) => item.Id, (StockMovement item) => item.MovementAtUtc, softDelete: false);
			List<ScheduleRegisterEntry> rows13 = await (from item in context.ScheduleRegisterEntries.AsNoTracking()
				where item.EntryAtUtc < cutoff
				select item).ToListAsync(cancellationToken);
			num = total;
			total = num + await WriteAsync<ScheduleRegisterEntry>("ScheduleRegisterEntries", rows13, (ScheduleRegisterEntry item) => item.Id, (ScheduleRegisterEntry item) => item.EntryAtUtc, softDelete: false);
			result = new ExportBundle(total, tableCounts, softDeleteIds, new _003C_003Ez__ReadOnlyArray<string>(new string[16]
			{
				"Sales", "SaleItems", "WholesaleInvoices", "WholesaleInvoiceItems", "PurchaseInvoices", "PurchaseItems", "PurchaseReturns", "PurchaseReturnItems", "ReturnNotes", "SaleReturnItems",
				"WholesaleReturnItems", "StockAdjustments", "ExpiryWriteOffs", "Receipts", "CustomerLedgerEntries", "SupplierLedgerEntries"
			}), new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "StockMovements", "ScheduleRegisterEntries" }));
		}
		finally
		{
			if (connection != null)
			{
				await connection.DisposeAsync();
			}
		}
		return result;
		async Task<long> WriteAsync<T>(string tableName, IEnumerable<T> enumerable, Func<T, Guid> idSelector, Func<T, DateTime> dateSelector, bool softDelete) where T : class
		{
			long count = 0L;
			List<Guid> ids = new List<Guid>();
			long result2;
			await using (DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken))
			{
				await using (SqliteCommand command = connection.CreateCommand())
				{
					command.Transaction = (SqliteTransaction)transaction;
					command.CommandText = "INSERT INTO ArchivedRows (TableName, Id, BusinessDate, PayloadJson, SoftDelete)\r\nVALUES ($table, $id, $date, $payload, $soft);";
					SqliteParameter tableParam = command.Parameters.Add("$table", SqliteType.Text);
					SqliteParameter idParam = command.Parameters.Add("$id", SqliteType.Text);
					SqliteParameter dateParam = command.Parameters.Add("$date", SqliteType.Text);
					SqliteParameter payloadParam = command.Parameters.Add("$payload", SqliteType.Text);
					SqliteParameter softParam = command.Parameters.Add("$soft", SqliteType.Integer);
					foreach (T item in enumerable)
					{
						cancellationToken.ThrowIfCancellationRequested();
						Guid id = idSelector(item);
						tableParam.Value = tableName;
						idParam.Value = id.ToString("D");
						dateParam.Value = dateSelector(item).ToString("O", CultureInfo.InvariantCulture);
						payloadParam.Value = JsonSerializer.Serialize(item, JsonOptions);
						softParam.Value = (softDelete ? 1 : 0);
						await command.ExecuteNonQueryAsync(cancellationToken);
						ids.Add(id);
						count++;
					}
				}
				await transaction.CommitAsync(cancellationToken);
				tableCounts[tableName] = count;
				if (softDelete && ids.Count > 0)
				{
					softDeleteIds[tableName] = ids;
				}
				result2 = count;
			}
			return result2;
		}
	}

	private async Task<long> SoftDeleteExportedDocumentsAsync(DateTime cutoff, Dictionary<string, List<Guid>> softDeleteIds, CancellationToken cancellationToken)
	{
		long deleted = 0L;
		DateTime now = DateTime.UtcNow;
		await SoftDeleteSetAsync<Sale>("Sales", context.Sales);
		await SoftDeleteSetAsync<SaleItem>("SaleItems", context.SaleItems);
		await SoftDeleteSetAsync<WholesaleInvoice>("WholesaleInvoices", context.WholesaleInvoices);
		await SoftDeleteSetAsync<WholesaleInvoiceItem>("WholesaleInvoiceItems", context.WholesaleInvoiceItems);
		await SoftDeleteSetAsync<PurchaseInvoice>("PurchaseInvoices", context.PurchaseInvoices);
		await SoftDeleteSetAsync<PurchaseItem>("PurchaseItems", context.PurchaseItems);
		await SoftDeleteSetAsync<PurchaseReturn>("PurchaseReturns", context.PurchaseReturns);
		await SoftDeleteSetAsync<PurchaseReturnItem>("PurchaseReturnItems", context.PurchaseReturnItems);
		await SoftDeleteSetAsync<ReturnNote>("ReturnNotes", context.ReturnNotes);
		await SoftDeleteSetAsync<SaleReturnItem>("SaleReturnItems", context.SaleReturnItems);
		await SoftDeleteSetAsync<WholesaleReturnItem>("WholesaleReturnItems", context.WholesaleReturnItems);
		await SoftDeleteSetAsync<StockAdjustment>("StockAdjustments", context.StockAdjustments);
		await SoftDeleteSetAsync<ExpiryWriteOff>("ExpiryWriteOffs", context.ExpiryWriteOffs);
		await SoftDeleteSetAsync<Receipt>("Receipts", context.Receipts);
		await SoftDeleteSetAsync<CustomerLedgerEntry>("CustomerLedgerEntries", context.CustomerLedgerEntries);
		await SoftDeleteSetAsync<SupplierLedgerEntry>("SupplierLedgerEntries", context.SupplierLedgerEntries);
		await context.SaveChangesAsync(cancellationToken);
		return deleted;
		async Task SoftDeleteSetAsync<TEntity>(string tableName, DbSet<TEntity> set) where TEntity : notnull, EntityBase
		{
			if (softDeleteIds.TryGetValue(tableName, out List<Guid> value) && value.Count != 0)
			{
				foreach (Guid[] item in value.Chunk(200))
				{
					HashSet<Guid> idSet = item.ToHashSet();
					foreach (TEntity item2 in await set.Where((TEntity item) => idSet.Contains(item.Id)).ToListAsync(cancellationToken))
					{
						item2.IsDeleted = true;
						item2.UpdatedAtUtc = now;
						item2.SyncState = SyncState.Pending;
						deleted++;
					}
				}
			}
		}
	}

	private static async Task EncryptArchiveAsync(string archivePath, string destinationPath, string password, CancellationToken cancellationToken)
	{
		string fullDestination = Path.GetFullPath(destinationPath);
		Directory.CreateDirectory(Path.GetDirectoryName(fullDestination));
		string temporaryPath = $"{fullDestination}.{Guid.NewGuid():N}.tmp";
		byte[] salt = RandomNumberGenerator.GetBytes(16);
		byte[] iv = RandomNumberGenerator.GetBytes(16);
		byte[] keyMaterial = Rfc2898DeriveBytes.Pbkdf2(password, salt, 600000, HashAlgorithmName.SHA256, 64);
		try
		{
			await using (FileStream output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true))
			{
				await output.WriteAsync(Magic, cancellationToken);
				await output.WriteAsync(BitConverter.GetBytes(600000), cancellationToken);
				await output.WriteAsync(salt, cancellationToken);
				await output.WriteAsync(iv, cancellationToken);
				using Aes aes = Aes.Create();
				aes.KeySize = 256;
				aes.Mode = CipherMode.CBC;
				aes.Padding = PaddingMode.PKCS7;
				aes.Key = keyMaterial[..32];
				aes.IV = iv;
				await using CryptoStream crypto = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
				await using FileStream input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true);
				await input.CopyToAsync(crypto, cancellationToken);
				await crypto.FlushFinalBlockAsync(cancellationToken);
			}
			await using (FileStream output = new FileStream(temporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 131072, useAsync: true))
			{
				using HMACSHA256 hmac = new HMACSHA256(keyMaterial[32..]);
				byte[] buffer = new byte[131072];
				int inputCount;
				while ((inputCount = await output.ReadAsync(buffer, cancellationToken)) > 0)
				{
					hmac.TransformBlock(buffer, 0, inputCount, null, 0);
				}
				hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
				output.Position = output.Length;
				await output.WriteAsync(hmac.Hash, cancellationToken);
				await output.FlushAsync(cancellationToken);
			}
			File.Move(temporaryPath, fullDestination, overwrite: true);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(keyMaterial);
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
			}
		}
		catch
		{
		}
	}
}
