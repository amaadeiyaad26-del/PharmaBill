using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

/// <summary>
/// Clears inventory, sales, purchases, and statutory registers while keeping
/// pharmacy profile, users, licences, branches, storage locations, and the drug catalogue.
/// Does not inject demo stock. Uses hard DELETE so soft-delete filters cannot hide leftovers.
/// </summary>
public sealed class OperationalDataResetService(IUnitOfWork unitOfWork)
{
	private static readonly string[] TransactionalTables =
	[
		"SaleReturnItems",
		"WholesaleReturnItems",
		"ReturnNotes",
		"SaleItems",
		"Sales",
		"WholesaleInvoiceItems",
		"WholesaleInvoices",
		"Receipts",
		"CustomerLedgerEntries",
		"SupplierLedgerEntries",
		"PurchaseReturnItems",
		"PurchaseReturns",
		"PurchaseItems",
		"PurchaseInvoices",
		"StockTransferItems",
		"StockTransfers",
		"StockVerificationItems",
		"StockVerificationSessions",
		"StockAdjustments",
		"ExpiryWriteOffs",
		"StockLocationBalances",
		"StockMovements",
		"Batches",
		"DrugPackLevels",
		"CustomerCategoryPrices",
		"TradeDiscounts",
		"WholesaleSchemes",
		"WholesaleRateHistory",
		"ScheduleOverrides",
		"ScheduleRegisterEntries",
		"Prescriptions",
		"Patients",
		"CustomerLicences",
		"Customers",
		"Suppliers",
		"Drugs",
		"SyncConflicts",
		"ChangeLogs",
		"NumberSeries"
	];

	public async Task<OperationalDataResetResult> ClearAllTransactionalDataAsync(CancellationToken cancellationToken = default)
	{
		PharmaBillDbContext context = unitOfWork.Context;
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

		int removed = 0;
		foreach (string table in TransactionalTables)
		{
			try
			{
				removed += await context.Database.ExecuteSqlRawAsync(
					$"""DELETE FROM "{table}";""",
					cancellationToken);
			}
			catch (Exception)
			{
				// Table may not exist on older schemas.
			}
		}

		try
		{
			await context.Database.ExecuteSqlRawAsync(
				"""UPDATE "Drugs" SET "StockQuantity" = 0 WHERE "StockQuantity" <> 0;""",
				cancellationToken);
		}
		catch (Exception)
		{
		}

		context.AuditLogs.Add(new AuditLog
		{
			ActionAtUtc = DateTime.UtcNow,
			Action = "OperationalDataCleared",
			EntityName = "Database",
			Details = $"Hard-cleared transactional tables ({removed} row(s)). Profile, users, licences, and catalogue retained."
		});

		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		context.ChangeTracker.Clear();
		int stockLeft = await context.Batches.IgnoreQueryFilters().CountAsync(cancellationToken);
		int registersLeft = await context.ScheduleRegisterEntries.IgnoreQueryFilters().CountAsync(cancellationToken);
		return new OperationalDataResetResult(removed, stockLeft, registersLeft);
	}
}

public sealed record OperationalDataResetResult(int RemovedRowCount, int RemainingBatchCount, int RemainingRegisterCount);
