using System;

namespace PharmaBill.Core.Inventory;

public static class BatchExpiryStatuses
{
	public const int CriticalDays = 30;

	public const int WarningDays = 90;

	public static BatchExpiryStatus Evaluate(DateOnly? expiryDate, DateOnly today)
	{
		if (!expiryDate.HasValue)
		{
			return BatchExpiryStatus.Good;
		}
		int num = expiryDate.Value.DayNumber - today.DayNumber;
		if (num < 30)
		{
			return BatchExpiryStatus.Critical;
		}
		if (num <= 90)
		{
			return BatchExpiryStatus.Warning;
		}
		return BatchExpiryStatus.Good;
	}

	public static bool IsNearExpiry(DateOnly? expiryDate, DateOnly today)
	{
		BatchExpiryStatus batchExpiryStatus = Evaluate(expiryDate, today);
		if ((uint)(batchExpiryStatus - 1) <= 1u)
		{
			return true;
		}
		return false;
	}
}
