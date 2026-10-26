using Microsoft.EntityFrameworkCore;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

// Stock that leaves the shop is checked inside the write transaction that removes it. SQLite runs write
// transactions one at a time, so only a read taken there cannot be overtaken by another bill, a phone sync
// or an import landing between the check and the save.
internal static class StockOnHand
{
	public static async Task<Dictionary<Guid, decimal>> ByBatchAsync(PharmaBillDbContext context, IReadOnlyCollection<Guid> batchIds, Guid? locationId, CancellationToken cancellationToken)
	{
		if (context.Database.CurrentTransaction == null)
		{
			throw new InvalidOperationException("Stock must be read inside the transaction that removes it.");
		}
		Guid[] ids = batchIds.Distinct().ToArray();
		Dictionary<Guid, decimal> onHand = await (from movement in context.StockMovements
			where ids.Contains(movement.BatchId) && (locationId == null || movement.LocationId == locationId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum(movement => movement.QuantityChange)
			}).ToDictionaryAsync(row => row.BatchId, row => row.Quantity, cancellationToken);
		foreach (Guid batchId in ids)
		{
			onHand.TryAdd(batchId, 0m);
		}
		return onHand;
	}

	public static async Task<decimal> ForBatchAsync(PharmaBillDbContext context, Guid batchId, CancellationToken cancellationToken)
	{
		return (await ByBatchAsync(context, [batchId], null, cancellationToken))[batchId];
	}
}
