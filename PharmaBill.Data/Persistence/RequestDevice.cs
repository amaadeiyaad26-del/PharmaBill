
namespace PharmaBill.Data.Persistence;

/// <summary>The machine a piece of work is being done for.</summary>
public sealed record RequestDeviceIdentity(Guid DeviceId, string MachineName);

// On the server PC, a request from a billing terminal is saved under that terminal's identity, not the server's:
// every row it writes carries the terminal's device ID, and audit-log rows and register entries record its name.
// Outside a terminal request, work is this PC's own.
public static class RequestDevice
{
	private static readonly AsyncLocal<RequestDeviceIdentity?> CurrentIdentity = new AsyncLocal<RequestDeviceIdentity?>();

	public static RequestDeviceIdentity? Current => CurrentIdentity.Value;

	/// <summary>The name recorded for work done here: the terminal's while serving one, else this PC's.</summary>
	public static string MachineName => Current?.MachineName ?? Environment.MachineName;

	public static IDisposable Use(Guid deviceId, string machineName)
	{
		RequestDeviceIdentity? previous = CurrentIdentity.Value;
		CurrentIdentity.Value = new RequestDeviceIdentity(deviceId, machineName);
		return new Restore(previous);
	}

	private sealed class Restore(RequestDeviceIdentity? previous) : IDisposable
	{
		public void Dispose() => CurrentIdentity.Value = previous;
	}
}
