using System;

namespace PharmaBill.Core.Inventory;

public static class StockShortage
{
	public static bool IsShortage(decimal currentStock, decimal reorderLevel)
	{
		if (reorderLevel > 0m)
		{
			return currentStock <= reorderLevel;
		}
		return false;
	}

	public static decimal ResolveTargetStockLevel(decimal reorderLevel, decimal? explicitTarget = null)
	{
		if (explicitTarget > (decimal?)0m)
		{
			return explicitTarget.Value;
		}
		if (reorderLevel <= 0m)
		{
			return 0m;
		}
		return Math.Max(reorderLevel, reorderLevel * 2m);
	}

	public static decimal SuggestedOrderQuantity(decimal currentStock, decimal reorderLevel, decimal? explicitTarget = null)
	{
		if (!IsShortage(currentStock, reorderLevel))
		{
			return 0m;
		}
		decimal num = ResolveTargetStockLevel(reorderLevel, explicitTarget);
		return Math.Max(0m, num - currentStock);
	}
}
