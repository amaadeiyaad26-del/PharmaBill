
namespace PharmaBill.Core.Loyalty;

public static class LoyaltyPolicy
{
	/// <summary>Default: one point for every ₹100 of bill value.</summary>
	public const decimal DefaultAmountPerPoint = 100m;

	/// <summary>
	/// Whole points earned on a bill. Fractions of a block are not rounded up,
	/// so a ₹99.99 bill earns nothing.
	/// </summary>
	public static int PointsForAmount(decimal billAmount, decimal amountPerPoint = DefaultAmountPerPoint)
	{
		if (amountPerPoint <= 0m)
		{
			throw new ArgumentOutOfRangeException(nameof(amountPerPoint), "Points need a positive amount per point.");
		}
		if (billAmount <= 0m)
		{
			return 0;
		}
		decimal points = decimal.Floor(billAmount / amountPerPoint);
		return points > int.MaxValue ? int.MaxValue : (int)points;
	}
}
