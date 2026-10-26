
namespace PharmaBill.Core;

/// <summary>Supplies the current instant. Tests substitute a fixed one; see <see cref="BusinessClock.Use"/>.</summary>
public interface IClock
{
	DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
	public static readonly SystemClock Instance = new SystemClock();

	public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class FixedClock(DateTime utcNow) : IClock
{
	public DateTime UtcNow { get; set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
}

/// <summary>
/// Every business date is decided in the pharmacy's local time, Asia/Kolkata (IST), never in UTC and never in the
/// PC's own timezone setting: bill and invoice dates, daily totals, the financial year, register entries, report
/// ranges, expiry, credit periods and due dates. Instants are stored in UTC; this class turns them into IST dates
/// and IST dates into UTC ranges. The clock is replaceable per async flow, so tests run identically at any hour.
/// </summary>
public static class BusinessClock
{
	private static readonly AsyncLocal<IClock?> Override = new AsyncLocal<IClock?>();

	/// <summary>India Standard Time (UTC+05:30, no daylight saving).</summary>
	public static TimeZoneInfo TimeZone { get; } = ResolveIndiaTimeZone();

	/// <summary>The clock used when no <see cref="Use"/> scope is active. The app keeps the system clock; the test suite pins it.</summary>
	public static IClock DefaultClock { get; set; } = SystemClock.Instance;

	public static IClock Clock => Override.Value ?? DefaultClock;

	public static DateTime UtcNow => DateTime.SpecifyKind(Clock.UtcNow, DateTimeKind.Utc);

	/// <summary>Current IST wall-clock time (Kind Unspecified: it is not this PC's local time).</summary>
	public static DateTime Now => ToLocal(UtcNow);

	/// <summary>Today's date in IST.</summary>
	public static DateOnly Today => DateOnly.FromDateTime(Now);

	/// <summary>Replaces the clock for the current async flow until disposed. Parallel tests do not see each other's clock.</summary>
	public static IDisposable Use(IClock clock)
	{
		IClock? previous = Override.Value;
		Override.Value = clock;
		return new Restore(previous);
	}

	/// <summary>IST wall-clock time of a stored instant. Unspecified kind is treated as UTC (how EF returns *AtUtc columns).</summary>
	public static DateTime ToLocal(DateTime instant)
	{
		DateTime utc = instant.Kind switch
		{
			DateTimeKind.Local => instant.ToUniversalTime(),
			_ => DateTime.SpecifyKind(instant, DateTimeKind.Utc)
		};
		return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZone), DateTimeKind.Unspecified);
	}

	/// <summary>The IST date on which a stored instant falls.</summary>
	public static DateOnly ToLocalDate(DateTime instant) => DateOnly.FromDateTime(ToLocal(instant));

	/// <summary>The UTC instant at which an IST date begins (00:00 IST = 18:30 UTC the day before).</summary>
	public static DateTime StartOfDayUtc(DateOnly date) =>
		DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), TimeZone), DateTimeKind.Utc);

	/// <summary>The UTC instant of an IST wall-clock time.</summary>
	public static DateTime FromLocal(DateTime istWallClock) =>
		DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(istWallClock, DateTimeKind.Unspecified), TimeZone), DateTimeKind.Utc);

	/// <summary>Indian financial year (1 April - 31 March) as "2026-27".</summary>
	public static string FinancialYear(DateOnly date)
	{
		int start = date.Month >= 4 ? date.Year : date.Year - 1;
		return $"{start}-{(start + 1) % 100:D2}";
	}

	private static TimeZoneInfo ResolveIndiaTimeZone()
	{
		foreach (string id in new[] { "Asia/Kolkata", "India Standard Time" })
		{
			try
			{
				return TimeZoneInfo.FindSystemTimeZoneById(id);
			}
			catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
			{
			}
		}

		return TimeZoneInfo.CreateCustomTimeZone("Asia/Kolkata", TimeSpan.FromMinutes(330), "India Standard Time", "IST");
	}

	private sealed class Restore(IClock? previous) : IDisposable
	{
		public void Dispose() => Override.Value = previous;
	}
}

/// <summary>Shorthand for the conversions most queries need.</summary>
public static class BusinessDateExtensions
{
	/// <summary>The UTC instant at which this IST date begins; use for "from" and exclusive "to" bounds on *AtUtc columns.</summary>
	public static DateTime StartOfDayUtc(this DateOnly istDate) => BusinessClock.StartOfDayUtc(istDate);

	/// <summary>IST wall-clock time of a stored UTC instant.</summary>
	public static DateTime ToBusinessLocal(this DateTime instant) => BusinessClock.ToLocal(instant);

	/// <summary>The IST date of a stored UTC instant.</summary>
	public static DateOnly ToBusinessDate(this DateTime instant) => BusinessClock.ToLocalDate(instant);
}
