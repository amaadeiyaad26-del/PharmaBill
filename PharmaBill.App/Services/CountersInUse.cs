using PharmaBill.Core;
using PharmaBill.Sync.Terminals;

namespace PharmaBill.App.Services;

/// <summary>A billing counter with someone signed in to this server.</summary>
public sealed record CounterInUse(string Name, IReadOnlyList<string> SignedIn, DateTime LastActiveUtc);

/// <summary>
/// Before replacing the database (restore, a new store): which billing counters are signed in to this server, so the
/// owner sees that trading is going on and confirms, instead of discovering it from the counters afterwards.
/// </summary>
public sealed class CountersInUse(TerminalSessions sessions, TerminalDeviceStore devices, IConfirmationService confirmation)
{
	public IReadOnlyList<CounterInUse> Now()
	{
		Dictionary<Guid, string> names = devices.List().ToDictionary(device => device.Id, device => device.Name);
		return sessions.SignedIn()
			.GroupBy(session => session.DeviceId)
			.Select(group => new CounterInUse(
				names.TryGetValue(group.Key, out string? name) ? name : "A billing counter",
				group.Select(session => session.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
				group.Max(session => session.LastUsedUtc)))
			.OrderBy(counter => counter.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>The warning for <paramref name="action"/> while counters are signed in; null when none are.</summary>
	public string? Warning(string action, string consequence)
	{
		IReadOnlyList<CounterInUse> counters = Now();
		if (counters.Count == 0)
		{
			return null;
		}
		int people = counters.Sum(counter => counter.SignedIn.Count);
		string list = string.Join(Environment.NewLine, counters.Select(counter =>
			$"  • {counter.Name}: {string.Join(", ", counter.SignedIn)} (last active {counter.LastActiveUtc.ToBusinessLocal():hh:mm tt})"));
		return $"{counters.Count} billing {(counters.Count == 1 ? "counter is" : "counters are")} connected to this server, with {people} {(people == 1 ? "person" : "people")} signed in:"
			+ Environment.NewLine + list + Environment.NewLine + Environment.NewLine
			+ $"{action} now stops their billing while it runs. {consequence}" + Environment.NewLine + Environment.NewLine
			+ "Doing this in the middle of trading is almost always a mistake. Continue anyway?";
	}

	/// <summary>True when no counters are signed in, or the owner has seen them and chosen to continue.</summary>
	public bool ConfirmProceed(string action, string consequence) =>
		Warning(action, consequence) is not string warning || confirmation.Confirm(warning, "Billing counters are connected");
}
