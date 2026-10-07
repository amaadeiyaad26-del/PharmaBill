using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class StockTransferService(IUnitOfWork unitOfWork, NumberSeriesService numberSeries, StorageLocationService locations, IEntitlementService entitlement)
{
	public const string SeriesPrefix = "ST";

	public async Task<StockTransfer> ConfirmTransferAsync(SaveStockTransferInput input, Guid actingUserId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!PermissionMatrix.Allows(role, AppPermission.EnterStock))
		{
			throw new UnauthorizedAccessException("Your role cannot transfer stock.");
		}
		if (!(await entitlement.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Stock transfer is unavailable in read-only mode.");
		}
		if (input.SourceLocationId == input.DestinationLocationId)
		{
			throw new InvalidOperationException("Source and destination locations must be different.");
		}
		if (input.Items.Count == 0)
		{
			throw new InvalidOperationException("Add at least one batch to transfer.");
		}
		await locations.EnsureDefaultRetailLocationAsync(cancellationToken);
		StorageLocation source = await unitOfWork.Context.StorageLocations.SingleAsync((StorageLocation item) => item.Id == input.SourceLocationId && item.IsActive, cancellationToken);
		StorageLocation destination = await unitOfWork.Context.StorageLocations.SingleAsync((StorageLocation item) => item.Id == input.DestinationLocationId && item.IsActive, cancellationToken);
		Guid[] batchIds = input.Items.Select((StockTransferLineInput item) => item.BatchId).Distinct().ToArray();
		Dictionary<Guid, Batch> batches = await unitOfWork.Context.Batches.Where((Batch batch2) => batchIds.Contains(batch2.Id)).ToDictionaryAsync((Batch batch2) => batch2.Id, cancellationToken);
		if (batches.Count != batchIds.Length)
		{
			throw new InvalidOperationException("One or more transfer batches were not found.");
		}
		StockTransfer result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			string transferNumber = await numberSeries.AllocateAsync("ST", input.TransferDate, cancellationToken);
			StockTransfer transfer = new StockTransfer
			{
				TransferNumber = transferNumber,
				SourceLocationId = source.Id,
				DestinationLocationId = destination.Id,
				TransferDate = input.TransferDate,
				Notes = (string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()),
				Status = "Posted"
			};
			unitOfWork.Context.StockTransfers.Add(transfer);
			foreach (StockTransferLineInput line in input.Items)
			{
				if (line.Quantity <= 0m)
				{
					throw new InvalidOperationException("Transfer quantity must be greater than zero.");
				}
				Batch batch = batches[line.BatchId];
				decimal num = await StorageLocationService.GetLocationOnHandAsync(unitOfWork.Context, source.Id, batch.Id, cancellationToken);
				if (line.Quantity > num)
				{
					throw new InvalidOperationException($"Insufficient stock at {source.Name} for batch {batch.BatchNo} (available {num:0.##}).");
				}
				unitOfWork.Context.StockTransferItems.Add(new StockTransferItem
				{
					StockTransferId = transfer.Id,
					BatchId = batch.Id,
					DrugId = batch.DrugId,
					QuantityTransferred = line.Quantity
				});
				unitOfWork.Context.StockMovements.Add(new StockMovement
				{
					BatchId = batch.Id,
					DrugId = batch.DrugId,
					LocationId = source.Id,
					QuantityChange = -line.Quantity,
					MovementType = "TransferOut",
					ReferenceType = "StockTransfer",
					ReferenceId = transfer.Id,
					MovementAtUtc = DateTime.UtcNow,
					Notes = transferNumber
				});
				unitOfWork.Context.StockMovements.Add(new StockMovement
				{
					BatchId = batch.Id,
					DrugId = batch.DrugId,
					LocationId = destination.Id,
					QuantityChange = line.Quantity,
					MovementType = "TransferIn",
					ReferenceType = "StockTransfer",
					ReferenceId = transfer.Id,
					MovementAtUtc = DateTime.UtcNow,
					Notes = transferNumber
				});
				await StorageLocationService.ApplyBalanceDeltaAsync(unitOfWork.Context, source.Id, batch.Id, batch.DrugId, -line.Quantity, cancellationToken);
				await StorageLocationService.ApplyBalanceDeltaAsync(unitOfWork.Context, destination.Id, batch.Id, batch.DrugId, line.Quantity, cancellationToken);
			}
			unitOfWork.Context.AuditLogs.Add(new AuditLog
			{
				UserId = actingUserId,
				Action = "StockTransferPosted",
				EntityName = "StockTransfer",
				EntityId = transfer.Id,
				Details = $"{transferNumber}: {source.Name} → {destination.Name}; {input.Items.Count} line(s)"
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = transfer;
		}
		return result;
	}

	public async Task<StockTransferSlip?> GetTransferSlipAsync(Guid transferId, CancellationToken cancellationToken = default(CancellationToken))
	{
		StockTransfer transfer = await unitOfWork.Context.StockTransfers.AsNoTracking().SingleOrDefaultAsync((StockTransfer item) => item.Id == transferId, cancellationToken);
		if (transfer == null)
		{
			return null;
		}
		Dictionary<Guid, StorageLocation> locations2 = await (from item in unitOfWork.Context.StorageLocations.AsNoTracking()
			where item.Id == transfer.SourceLocationId || item.Id == transfer.DestinationLocationId
			select item).ToDictionaryAsync((StorageLocation item) => item.Id, cancellationToken);
		List<StockTransferItem> items = await (from item in unitOfWork.Context.StockTransferItems.AsNoTracking()
			where item.StockTransferId == transferId
			select item).ToListAsync(cancellationToken);
		Guid[] batchIds = items.Select((StockTransferItem item) => item.BatchId).ToArray();
		Dictionary<Guid, Batch> batches = await (from batch in unitOfWork.Context.Batches.AsNoTracking()
			where batchIds.Contains(batch.Id)
			select batch).ToDictionaryAsync((Batch batch) => batch.Id, cancellationToken);
		Guid[] drugIds = items.Select((StockTransferItem item) => item.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await (from drug in unitOfWork.Context.Drugs.AsNoTracking()
			where drugIds.Contains(drug.Id)
			select drug).ToDictionaryAsync((Drug drug) => drug.Id, cancellationToken);
		return new StockTransferSlip(transfer.Id, transfer.TransferNumber, locations2.GetValueOrDefault(transfer.SourceLocationId)?.Name ?? "Source", locations2.GetValueOrDefault(transfer.DestinationLocationId)?.Name ?? "Destination", transfer.TransferDate, transfer.Notes, items.Select((StockTransferItem item) =>
		{
			Batch batch = batches[item.BatchId];
			return new StockTransferSlipLine(drugs.GetValueOrDefault(item.DrugId)?.Name ?? "Medicine", batch.BatchNo, batch.ExpiryDate, item.QuantityTransferred, batch.Rack);
		}).ToArray());
	}

	public async Task<IReadOnlyList<(Guid BatchId, string DrugName, string BatchNo, DateOnly? Expiry, decimal Quantity, string? Rack)>> GetSaleableBatchesAtLocationAsync(Guid locationId, string? medicineFilter = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		var list = await (from movement in unitOfWork.Context.StockMovements.AsNoTracking()
			where movement.LocationId == locationId
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement item) => item.QuantityChange)
			} into item
			where item.Quantity > 0m
			select item).ToListAsync(cancellationToken);
		if (list.Count == 0)
		{
			return Array.Empty<(Guid, string, string, DateOnly?, decimal, string)>();
		}
		Guid[] batchIds = list.Select(item => item.BatchId).ToArray();
		Dictionary<Guid, decimal> qty = list.ToDictionary(item => item.BatchId, item => item.Quantity);
		List<Batch> batches = await (from batch in unitOfWork.Context.Batches.AsNoTracking()
			where batchIds.Contains(batch.Id) && (batch.ExpiryDate == null || batch.ExpiryDate >= today)
			select batch).ToListAsync(cancellationToken);
		Guid[] drugIds = batches.Select((Batch batch) => batch.DrugId).Distinct().ToArray();
		Dictionary<Guid, Drug> drugs = await (from drug in unitOfWork.Context.Drugs.AsNoTracking()
			where drugIds.Contains(drug.Id) && drug.IsActive
			select drug).ToDictionaryAsync((Drug drug) => drug.Id, cancellationToken);
		string filter = medicineFilter?.Trim() ?? string.Empty;
		return (from batch in batches
			where drugs.ContainsKey(batch.DrugId)
			select (Id: batch.Id, Name: drugs[batch.DrugId].Name, BatchNo: batch.BatchNo, ExpiryDate: batch.ExpiryDate, qty.GetValueOrDefault(batch.Id), Rack: batch.Rack) into item
			where filter.Length == 0 || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || item.BatchNo.Contains(filter, StringComparison.OrdinalIgnoreCase)
			select item).OrderBy(((Guid Id, string Name, string BatchNo, DateOnly? ExpiryDate, decimal, string Rack) item) => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(((Guid Id, string Name, string BatchNo, DateOnly? ExpiryDate, decimal, string Rack) item) => item.ExpiryDate ?? DateOnly.MaxValue).ToArray();
	}
}
