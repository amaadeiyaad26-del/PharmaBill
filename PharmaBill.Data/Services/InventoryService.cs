using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Inventory;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class InventoryService(PharmaBillDbContext context, IUnitOfWork unitOfWork, IEntitlementService entitlement)
{
	public async Task<IReadOnlyList<StockDrugRow>> GetStockAsync(string? schedule = null, string? company = null, Guid? supplierId = null, string? status = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		List<Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive
			select drug).ToListAsync(cancellationToken);
		Guid[] drugIds = drugs.Select((Drug drug) => drug.Id).ToArray();
		List<Batch> batches = await (from batch in context.Batches.AsNoTracking()
			where drugIds.Contains(batch.DrugId)
			select batch).ToListAsync(cancellationToken);
		Guid[] batchIds = batches.Select((Batch batch) => batch.Id).ToArray();
		Dictionary<Guid, decimal> movementQuantities = await (from movement in context.StockMovements.AsNoTracking()
			where batchIds.Contains(movement.BatchId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement movement) => movement.QuantityChange)
			}).ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
		Guid[] supplierIds = (from batch in batches
			where batch.SupplierId.HasValue
			select batch.SupplierId.Value).Distinct().ToArray();
		Dictionary<Guid, string> suppliers = await (from supplier in context.Suppliers.AsNoTracking()
			where supplierIds.Contains(supplier.Id)
			select supplier).ToDictionaryAsync((Supplier supplier) => supplier.Id, (Supplier supplier) => supplier.Name, cancellationToken);
		Dictionary<Guid, Batch[]> batchesByDrug = (from batch in batches
			group batch by batch.DrugId).ToDictionary((IGrouping<Guid, Batch> group) => group.Key, (IGrouping<Guid, Batch> group) => group.ToArray());
		Guid[] catalogIds = (from drug in drugs
			where drug.CatalogMedicineId.HasValue
			select drug.CatalogMedicineId.Value).Distinct().ToArray();
		Dictionary<Guid, string> manufacturers = await (from medicine in context.CatalogMedicines.AsNoTracking()
			where catalogIds.Contains(medicine.Id)
			select medicine).ToDictionaryAsync((CatalogMedicine medicine) => medicine.Id, (CatalogMedicine medicine) => medicine.Manufacturer, cancellationToken);
		return (from row in drugs.Select((Drug drug) =>
			{
				Batch[] valueOrDefault = batchesByDrug.GetValueOrDefault(drug.Id, Array.Empty<Batch>());
				decimal totalStock = valueOrDefault.Sum((Batch batch) => movementQuantities.GetValueOrDefault(batch.Id));
				string manufacturer = (drug.CatalogMedicineId.HasValue ? manufacturers.GetValueOrDefault(drug.CatalogMedicineId.Value) : null);
				return new StockDrugRow(drug.Id, drug.Name, drug.Schedule, manufacturer, valueOrDefault.Select((Batch batch) => (!batch.SupplierId.HasValue) ? null : suppliers.GetValueOrDefault(batch.SupplierId.Value)).FirstOrDefault((string name) => name != null), totalStock, drug.ReorderLevel, (from batch in valueOrDefault.OrderBy((Batch batch) => batch.ExpiryDate ?? DateOnly.MaxValue).ThenBy((Batch batch) => batch.BatchNo, StringComparer.OrdinalIgnoreCase)
					select new StockBatchRow(batch.Id, batch.BatchNo, batch.ExpiryDate, batch.Mrp.GetValueOrDefault(), batch.Ptr, batch.PurchasePrice, movementQuantities.GetValueOrDefault(batch.Id), batch.Rack, batch.IsBanned, batch.BanReason)).ToArray(), drug.IsBanned);
			})
			where string.IsNullOrWhiteSpace(schedule) || string.Equals(row.Schedule, schedule, StringComparison.OrdinalIgnoreCase)
			where string.IsNullOrWhiteSpace(company) || (row.Manufacturer?.Contains(company, StringComparison.OrdinalIgnoreCase) ?? false)
			where !supplierId.HasValue || (batchesByDrug.TryGetValue(row.DrugId, out var value) && value.Any((Batch batch) => batch.SupplierId == supplierId.Value))
			select row).Where((StockDrugRow row) =>
		{
			switch (status)
			{
			case "Expired":
			case "Critical":
				return row.Batches.Any((StockBatchRow batch) => batch.ExpiryBand == BatchExpiryStatus.Critical);
			case "Warning":
			case "Near expiry":
				return row.Batches.Any((StockBatchRow batch) => batch.IsNearExpiry);
			case "Reorder":
			case "Shortage":
			case "Low stock":
				return row.IsShortage;
			case "In stock":
				return row.TotalStock > 0m;
			case "Out of stock":
				return row.TotalStock <= 0m;
			default:
				return true;
			}
		}).OrderBy((StockDrugRow row) => row.DrugName, StringComparer.OrdinalIgnoreCase).ToArray();
	}

	public async Task AdjustStockAsync(Guid batchId, decimal quantityChange, string reason, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.EnterStock))
		{
			throw new UnauthorizedAccessException("Your role cannot adjust stock.");
		}
		if (!(await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Stock entry is unavailable in read-only mode.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		if (quantityChange == 0m)
		{
			throw new ArgumentOutOfRangeException("quantityChange", "Adjustment quantity cannot be zero.");
		}
		Batch batch = await context.Batches.SingleAsync((Batch item) => item.Id == batchId, cancellationToken);
		string oldSnapshot = ComplianceAuditWriter.Snapshot(new { batch.BatchNo, batch.Quantity, batch.Mrp, batch.DrugId });
		decimal onHand = (await context.StockMovements.Where((StockMovement movement) => movement.BatchId == batchId).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault();
		if (onHand + quantityChange < 0m)
		{
			throw new InvalidOperationException("Adjustment cannot reduce batch stock below zero.");
		}
		RecordLockGuard.Demand(batch.CreatedAtUtc, "BatchStock", batch.Id, "MODIFIED_AFTER_LOCK");
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		batch.Quantity = onHand + quantityChange;
		StockAdjustment stockAdjustment = new StockAdjustment
		{
			BatchId = batch.Id,
			DrugId = batch.DrugId,
			QuantityChange = quantityChange,
			Reason = reason.Trim()
		};
		context.StockAdjustments.Add(stockAdjustment);
		context.StockMovements.Add(new StockMovement
		{
			BatchId = batch.Id,
			DrugId = batch.DrugId,
			QuantityChange = quantityChange,
			MovementType = "Adjustment",
			ReferenceType = "StockAdjustment",
			ReferenceId = stockAdjustment.Id,
			Notes = reason.Trim()
		});
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			Action = "StockAdjusted",
			EntityName = "Batch",
			EntityId = batch.Id,
			Details = $"Quantity change: {quantityChange}; reason: {reason.Trim()}"
		});
		if (quantityChange > 0m)
		{
			SyncConflict syncConflict = await context.SyncConflicts.IgnoreQueryFilters().SingleOrDefaultAsync((SyncConflict item) => item.EntityName == "StockConflict" && item.EntityId == batch.Id && item.Resolution == "Open", cancellationToken);
			if (syncConflict != null)
			{
				DateTime utcNow = DateTime.UtcNow;
				syncConflict.Resolution = "Resolved by stock adjustment: " + reason.Trim();
				syncConflict.ResolvedAtUtc = utcNow;
				syncConflict.UpdatedAtUtc = utcNow;
			}
		}
		await ComplianceAuditWriter.AppendIfUnlockedAsync(context, batch.CreatedAtUtc, new ComplianceAuditEntry
		{
			EntityType = "BatchStock",
			EntityId = batch.Id,
			Action = "MODIFIED_AFTER_LOCK",
			AuthorizedBy = string.Empty,
			Reason = reason.Trim(),
			OldSnapshotJson = oldSnapshot,
			NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { batch.BatchNo, batch.Quantity, quantityChange, batch.Mrp }),
			UserId = userId
		}, cancellationToken);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task<IReadOnlyList<ExpiredBatchRow>> GetExpiredBatchesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<Batch> expired = await (from batch in context.Batches.AsNoTracking()
			where batch.ExpiryDate != null && batch.ExpiryDate < today
			select batch).ToListAsync(cancellationToken);
		Guid[] ids = expired.Select((Batch batch) => batch.Id).ToArray();
		Dictionary<Guid, decimal> quantities = await (from movement in context.StockMovements.AsNoTracking()
			where ids.Contains(movement.BatchId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement movement) => movement.QuantityChange)
			}).ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
		Guid[] drugIds = expired.Select((Batch batch) => batch.DrugId).Distinct().ToArray();
		Dictionary<Guid, string> names = await (from drug in context.Drugs.AsNoTracking()
			where drugIds.Contains(drug.Id)
			select drug).ToDictionaryAsync((Drug drug) => drug.Id, (Drug drug) => drug.Name, cancellationToken);
		return (from batch in expired
			select new ExpiredBatchRow(batch.Id, names.GetValueOrDefault(batch.DrugId, string.Empty), batch.BatchNo, batch.ExpiryDate.Value, quantities.GetValueOrDefault(batch.Id)) into row
			where row.Quantity > 0m
			select row).OrderBy((ExpiredBatchRow row) => row.DrugName, StringComparer.OrdinalIgnoreCase).ThenBy((ExpiredBatchRow row) => row.ExpiryDate).ToArray();
	}

	public async Task<int> WriteOffAllExpiredAsync(string reason, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.EnterStock))
		{
			throw new UnauthorizedAccessException("Your role cannot write off stock.");
		}
		if (!(await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Stock entry is unavailable in read-only mode.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		IReadOnlyList<ExpiredBatchRow> rows = await GetExpiredBatchesAsync(cancellationToken);
		if (rows.Count == 0)
		{
			return 0;
		}
		Guid[] batchIds = rows.Select(row => row.BatchId).ToArray();
		List<Batch> lockedCheck = await context.Batches.Where(item => batchIds.Contains(item.Id)).ToListAsync(cancellationToken);
		foreach (Batch lockedBatch in lockedCheck)
		{
			RecordLockGuard.Demand(lockedBatch.CreatedAtUtc, "BatchStock", lockedBatch.Id, "MODIFIED_AFTER_LOCK");
		}
		int count;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			foreach (ExpiredBatchRow row in rows)
			{
				Batch batch = await context.Batches.SingleAsync((Batch item) => item.Id == row.BatchId, cancellationToken);
				string oldSnapshot = ComplianceAuditWriter.Snapshot(new { batch.BatchNo, batch.Quantity, WrittenOff = row.Quantity });
				batch.Quantity = 0m;
				context.ExpiryWriteOffs.Add(new ExpiryWriteOff
				{
					BatchId = batch.Id,
					DrugId = batch.DrugId,
					Quantity = row.Quantity,
					ExpiryDate = row.ExpiryDate,
					Reason = reason.Trim()
				});
				context.StockMovements.Add(new StockMovement
				{
					BatchId = batch.Id,
					DrugId = batch.DrugId,
					QuantityChange = -row.Quantity,
					MovementType = "ExpiryWriteOff",
					ReferenceType = "ExpiryWriteOff",
					ReferenceId = batch.Id,
					Notes = reason.Trim()
				});
				context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
				{
					RegisterType = "ExpiryDump",
					DrugId = batch.DrugId,
					BatchId = batch.Id,
					Quantity = row.Quantity,
					BatchNo = batch.BatchNo,
					EntryAtUtc = DateTime.UtcNow,
					Notes = reason.Trim()
				});
				context.AuditLogs.Add(new AuditLog
				{
					UserId = userId,
					Action = "ExpiredStockWrittenOff",
					EntityName = "Batch",
					EntityId = batch.Id,
					Details = $"Quantity: {row.Quantity}; reason: {reason.Trim()}"
				});
				await ComplianceAuditWriter.AppendIfUnlockedAsync(context, batch.CreatedAtUtc, new ComplianceAuditEntry
				{
					EntityType = "BatchStock",
					EntityId = batch.Id,
					Action = "MODIFIED_AFTER_LOCK",
					AuthorizedBy = string.Empty,
					Reason = reason.Trim(),
					OldSnapshotJson = oldSnapshot,
					NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { batch.BatchNo, Quantity = 0m, WrittenOff = row.Quantity }),
					UserId = userId
				}, cancellationToken);
			}
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			count = rows.Count;
		}
		return count;
	}

	public async Task WriteOffExpiredBatchAsync(Guid batchId, decimal quantity, string reason, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.EnterStock))
		{
			throw new UnauthorizedAccessException("Your role cannot write off stock.");
		}
		if (!(await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Stock entry is unavailable in read-only mode.");
		}
		ArgumentException.ThrowIfNullOrWhiteSpace(reason, "reason");
		if (quantity <= 0m)
		{
			throw new ArgumentOutOfRangeException("quantity");
		}
		Batch batch = await context.Batches.SingleAsync((Batch item) => item.Id == batchId, cancellationToken);
		string oldSnapshot = ComplianceAuditWriter.Snapshot(new { batch.BatchNo, batch.Quantity, batch.ExpiryDate });
		DateOnly dateOnly = DateOnly.FromDateTime(DateTime.Today);
		if (!batch.ExpiryDate.HasValue || batch.ExpiryDate.Value >= dateOnly)
		{
			throw new InvalidOperationException("Only expired batches can be written off.");
		}
		decimal onHand = (await context.StockMovements.Where((StockMovement movement) => movement.BatchId == batchId).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault();
		if (quantity > onHand)
		{
			throw new InvalidOperationException("Write-off quantity exceeds current batch stock.");
		}
		RecordLockGuard.Demand(batch.CreatedAtUtc, "BatchStock", batch.Id, "MODIFIED_AFTER_LOCK");
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		batch.Quantity = onHand - quantity;
		context.ExpiryWriteOffs.Add(new ExpiryWriteOff
		{
			BatchId = batch.Id,
			DrugId = batch.DrugId,
			Quantity = quantity,
			ExpiryDate = batch.ExpiryDate.Value,
			Reason = reason.Trim()
		});
		context.StockMovements.Add(new StockMovement
		{
			BatchId = batch.Id,
			DrugId = batch.DrugId,
			QuantityChange = -quantity,
			MovementType = "ExpiryWriteOff",
			ReferenceType = "ExpiryWriteOff",
			ReferenceId = batch.Id,
			Notes = reason.Trim()
		});
		context.ScheduleRegisterEntries.Add(new ScheduleRegisterEntry
		{
			RegisterType = "ExpiryDump",
			DrugId = batch.DrugId,
			BatchId = batch.Id,
			Quantity = quantity,
			BatchNo = batch.BatchNo,
			EntryAtUtc = DateTime.UtcNow,
			Notes = reason.Trim()
		});
		context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			Action = "ExpiredStockWrittenOff",
			EntityName = "Batch",
			EntityId = batch.Id,
			Details = $"Quantity: {quantity}; reason: {reason.Trim()}"
		});
		await ComplianceAuditWriter.AppendIfUnlockedAsync(context, batch.CreatedAtUtc, new ComplianceAuditEntry
		{
			EntityType = "BatchStock",
			EntityId = batch.Id,
			Action = "MODIFIED_AFTER_LOCK",
			AuthorizedBy = string.Empty,
			Reason = reason.Trim(),
			OldSnapshotJson = oldSnapshot,
			NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { batch.BatchNo, batch.Quantity, WrittenOff = quantity }),
			UserId = userId
		}, cancellationToken);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	public async Task UpdateBatchMrpAsync(Guid batchId, decimal mrp, CancellationToken cancellationToken = default)
	{
		if (mrp < 0m)
		{
			throw new ArgumentOutOfRangeException(nameof(mrp), "MRP cannot be negative.");
		}

		Batch batch = await context.Batches.SingleAsync(item => item.Id == batchId, cancellationToken);
		string oldSnapshot = ComplianceAuditWriter.Snapshot(new { batch.BatchNo, batch.Mrp, batch.Quantity });
		RecordLockGuard.Demand(batch.CreatedAtUtc, "BatchStock", batch.Id, "MODIFIED_AFTER_LOCK");
		decimal previousMrp = batch.Mrp ?? 0m;
		batch.Mrp = mrp;
		Drug? drug = await context.Drugs.SingleOrDefaultAsync(item => item.Id == batch.DrugId, cancellationToken);
		if (drug != null)
		{
			drug.Mrp = mrp;
		}

		await ComplianceAuditWriter.AppendIfUnlockedAsync(context, batch.CreatedAtUtc, new ComplianceAuditEntry
		{
			EntityType = "BatchStock",
			EntityId = batch.Id,
			Action = "MODIFIED_AFTER_LOCK",
			AuthorizedBy = string.Empty,
			Reason = "MRP override",
			OldSnapshotJson = oldSnapshot,
			NewSnapshotJson = ComplianceAuditWriter.Snapshot(new { batch.BatchNo, PreviousMrp = previousMrp, batch.Mrp }),
			UserId = RecordUnlockContext.Current?.AdminUserId
		}, cancellationToken);
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}
}
