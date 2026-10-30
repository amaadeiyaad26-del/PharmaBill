namespace PharmaBill.Core.Inventory;

/// <summary>
/// A supplier's expiry-return window: returns accepted from <see cref="DaysBefore"/> days before a batch's expiry until
/// <see cref="DaysAfter"/> days after it, both ends included. "Today" is always the business date in Asia/Kolkata
/// (<see cref="BusinessClock.Today"/>), so the first and last claimable days do not move with the PC's time zone.
/// </summary>
public readonly record struct ExpiryReturnWindow(int DaysBefore, int DaysAfter)
{
	public const int MaxDays = 3650;

	/// <summary>The first business date a batch expiring on <paramref name="expiry"/> can be claimed.</summary>
	public DateOnly OpensOn(DateOnly expiry) => expiry.AddDays(-DaysBefore);

	/// <summary>The last business date it can be claimed.</summary>
	public DateOnly ClosesOn(DateOnly expiry) => expiry.AddDays(DaysAfter);

	public bool Accepts(DateOnly expiry, DateOnly today) => today >= OpensOn(expiry) && today <= ClosesOn(expiry);

	/// <summary>Plain words for the window, as printed on a claim.</summary>
	public string Describe() =>
		$"Returns accepted from {DaysBefore} day{(DaysBefore == 1 ? "" : "s")} before expiry to {DaysAfter} day{(DaysAfter == 1 ? "" : "s")} after expiry";

	/// <summary>Throws unless both ends are whole days from 0 to <see cref="MaxDays"/>.</summary>
	public static ExpiryReturnWindow Validated(int daysBefore, int daysAfter)
	{
		if (daysBefore is < 0 or > MaxDays || daysAfter is < 0 or > MaxDays)
		{
			throw new ArgumentOutOfRangeException(nameof(daysBefore), $"An expiry-return window is 0 to {MaxDays} days on each side of expiry.");
		}
		return new ExpiryReturnWindow(daysBefore, daysAfter);
	}
}
