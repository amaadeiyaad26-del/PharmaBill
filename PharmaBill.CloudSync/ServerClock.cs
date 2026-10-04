using PharmaBill.Core.Sync;

namespace PharmaBill.CloudSync;

// Reuses the shared HLC state so stamps match the Windows and Android clients.
public sealed class ServerClock
{
    public static readonly Guid ServerDeviceId = new("00000000-0000-4000-8000-00000000c10d");

    private readonly HybridLogicalClockState _state = new();
    private readonly object _gate = new();

    public string Now()
    {
        lock (_gate)
        {
            return _state.Now(ServerDeviceId, DateTime.UtcNow);
        }
    }

    public string Receive(string remoteStamp)
    {
        lock (_gate)
        {
            return _state.Receive(remoteStamp, ServerDeviceId, DateTime.UtcNow);
        }
    }

    public void Observe(string stamp)
    {
        lock (_gate)
        {
            _state.Observe(stamp);
        }
    }
}
