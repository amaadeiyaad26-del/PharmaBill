using System;

namespace PharmaBill.Core;

public static class Money
{
	public static long ToPaise(decimal amount)
	{
		return (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
	}

	public static decimal FromPaise(long paise)
	{
		return (decimal)paise / 100m;
	}
}
