using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class StorageLocationService(PharmaBillDbContext context)
{
	public const string DefaultRetailCode = "SHOP";

	public const string DefaultRetailName = "Shop Front";

	public const string GodownCode = "GODOWN_A";

	public const string GodownName = "Basement Godown";

	public const string ColdStorageCode = "COLD";

	public const string ColdStorageName = "Cold Storage";

	public async Task<StorageLocation> EnsureDefaultRetailLocationAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		StorageLocation existing = await context.StorageLocations.FirstOrDefaultAsync((StorageLocation item) => item.IsDefaultRetailLocation && item.IsActive, cancellationToken);
		if (existing != null)
		{
			await EnsureStandardGodownsAsync(cancellationToken);
			await BackfillNullMovementLocationsAsync(existing.Id, cancellationToken);
			return existing;
		}
		existing = await context.StorageLocations.FirstOrDefaultAsync((StorageLocation item) => item.Code == "SHOP", cancellationToken);
		if (existing != null)
		{
			existing.IsDefaultRetailLocation = true;
			existing.IsActive = true;
			existing.Name = (string.IsNullOrWhiteSpace(existing.Name) ? "Shop Front" : existing.Name);
			await context.SaveChangesAsync(cancellationToken);
			await EnsureStandardGodownsAsync(cancellationToken);
			await BackfillNullMovementLocationsAsync(existing.Id, cancellationToken);
			return existing;
		}
		StorageLocation location = new StorageLocation
		{
			Name = "Shop Front",
			Code = "SHOP",
			IsDefaultRetailLocation = true,
			IsActive = true
		};
		context.StorageLocations.Add(location);
		await context.SaveChangesAsync(cancellationToken);
		await EnsureStandardGodownsAsync(cancellationToken);
		await BackfillNullMovementLocationsAsync(location.Id, cancellationToken);
		return location;
	}

	public async Task EnsureStandardGodownsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await EnsureLocationIfMissingAsync("Basement Godown", "GODOWN_A", isDefaultRetail: false, cancellationToken);
		await EnsureLocationIfMissingAsync("Cold Storage", "COLD", isDefaultRetail: false, cancellationToken);
	}

	private async Task EnsureLocationIfMissingAsync(string name, string code, bool isDefaultRetail, CancellationToken cancellationToken)
	{
		if (!(await context.StorageLocations.AnyAsync((StorageLocation item) => item.Code == code, cancellationToken)))
		{
			context.StorageLocations.Add(new StorageLocation
			{
				Name = name,
				Code = code,
				IsDefaultRetailLocation = isDefaultRetail,
				IsActive = true
			});
			await context.SaveChangesAsync(cancellationToken);
		}
	}

	public async Task<IReadOnlyList<StorageLocation>> GetActiveLocationsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		await EnsureDefaultRetailLocationAsync(cancellationToken);
		return await (from item in context.StorageLocations.AsNoTracking()
			where item.IsActive
			orderby item.IsDefaultRetailLocation descending, item.Name
			select item).ToListAsync(cancellationToken);
	}

	public async Task<StorageLocation> GetDefaultRetailLocationAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return await EnsureDefaultRetailLocationAsync(cancellationToken);
	}

	public async Task<StorageLocation> AddLocationAsync(string name, string code, bool isDefaultRetail, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name, "name");
		ArgumentException.ThrowIfNullOrWhiteSpace(code, "code");
		await EnsureDefaultRetailLocationAsync(cancellationToken);
		string normalizedCode = code.Trim().ToUpperInvariant();
		if (await context.StorageLocations.AnyAsync((StorageLocation item) => item.Code == normalizedCode, cancellationToken))
		{
			throw new InvalidOperationException("A location with code " + normalizedCode + " already exists.");
		}
		if (isDefaultRetail)
		{
			foreach (StorageLocation item in await context.StorageLocations.Where((StorageLocation item) => item.IsDefaultRetailLocation).ToListAsync(cancellationToken))
			{
				item.IsDefaultRetailLocation = false;
			}
		}
		StorageLocation created = new StorageLocation
		{
			Name = name.Trim(),
			Code = normalizedCode,
			IsDefaultRetailLocation = isDefaultRetail,
			IsActive = true
		};
		context.StorageLocations.Add(created);
		await context.SaveChangesAsync(cancellationToken);
		return created;
	}

	public static async Task<decimal> ApplyBalanceDeltaAsync(PharmaBillDbContext db, Guid locationId, Guid batchId, Guid drugId, decimal quantityDelta, CancellationToken cancellationToken = default(CancellationToken))
	{
		StockLocationBalance stockLocationBalance = db.StockLocationBalances.Local.FirstOrDefault((StockLocationBalance item) => item.LocationId == locationId && item.BatchId == batchId && !item.IsDeleted);
		if (stockLocationBalance == null)
		{
			stockLocationBalance = await db.StockLocationBalances.SingleOrDefaultAsync((StockLocationBalance item) => item.LocationId == locationId && item.BatchId == batchId, cancellationToken);
		}
		StockLocationBalance stockLocationBalance2 = stockLocationBalance;
		if (stockLocationBalance2 == null)
		{
			stockLocationBalance2 = new StockLocationBalance
			{
				LocationId = locationId,
				BatchId = batchId,
				DrugId = drugId,
				Quantity = 0m
			};
			db.StockLocationBalances.Add(stockLocationBalance2);
		}
		decimal num = stockLocationBalance2.Quantity + quantityDelta;
		if (num < 0m)
		{
			throw new InvalidOperationException("Location stock cannot go below zero.");
		}
		stockLocationBalance2.Quantity = num;
		return num;
	}

	public static async Task<decimal> GetLocationOnHandAsync(PharmaBillDbContext db, Guid locationId, Guid batchId, CancellationToken cancellationToken = default(CancellationToken))
	{
		return (await (from movement in db.StockMovements.AsNoTracking()
			where movement.BatchId == batchId && movement.LocationId == locationId
			select movement).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault();
	}

	private async Task BackfillNullMovementLocationsAsync(Guid defaultLocationId, CancellationToken cancellationToken)
	{
		List<StockMovement> list = await context.StockMovements.Where((StockMovement movement) => movement.LocationId == null).ToListAsync(cancellationToken);
		if (list.Count == 0)
		{
			return;
		}
		foreach (StockMovement item in list)
		{
			item.LocationId = defaultLocationId;
		}
		var enumerable = from movement in list
			group movement by new { movement.BatchId, movement.DrugId } into @group
			select new
			{
				BatchId = @group.Key.BatchId,
				DrugId = @group.Key.DrugId,
				Quantity = @group.Sum((StockMovement item) => item.QuantityChange)
			};
		foreach (var item2 in enumerable)
		{
			if (!(item2.Quantity == 0m))
			{
				await ApplyBalanceDeltaAsync(context, defaultLocationId, item2.BatchId, item2.DrugId, item2.Quantity, cancellationToken);
			}
		}
		await context.SaveChangesAsync(cancellationToken);
	}
}
