using System.Globalization;

namespace PharmaBill.Core.Sync;

public readonly record struct HlcValue(long PhysicalMilliseconds, long LogicalCounter, Guid DeviceId)
{
    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{PhysicalMilliseconds}:{LogicalCounter}:{DeviceId:D}").ToLowerInvariant();
}

public sealed class HybridLogicalClockState
{
    private readonly object _gate = new();
    private long _physicalMilliseconds;
    private long _logicalCounter;

    public string Now(Guid deviceId, DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("HLC wall time must be UTC.", nameof(utcNow));
        }

        var wallMilliseconds = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
        lock (_gate)
        {
            if (wallMilliseconds > _physicalMilliseconds)
            {
                _physicalMilliseconds = wallMilliseconds;
                _logicalCounter = 0;
            }
            else
            {
                _logicalCounter++;
            }

            return new HlcValue(_physicalMilliseconds, _logicalCounter, deviceId).ToString();
        }
    }

    public string Receive(string remoteStamp, Guid localDeviceId, DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("HLC wall time must be UTC.", nameof(utcNow));
        }

        var remote = Parse(remoteStamp);
        var wallMilliseconds = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
        lock (_gate)
        {
            var maxPhysical = Math.Max(wallMilliseconds, Math.Max(_physicalMilliseconds, remote.PhysicalMilliseconds));
            if (maxPhysical == _physicalMilliseconds && maxPhysical == remote.PhysicalMilliseconds)
            {
                _logicalCounter = Math.Max(_logicalCounter, remote.LogicalCounter) + 1;
            }
            else if (maxPhysical == _physicalMilliseconds)
            {
                _logicalCounter++;
            }
            else if (maxPhysical == remote.PhysicalMilliseconds)
            {
                _logicalCounter = remote.LogicalCounter + 1;
            }
            else
            {
                _logicalCounter = 0;
            }

            _physicalMilliseconds = maxPhysical;
            return new HlcValue(_physicalMilliseconds, _logicalCounter, localDeviceId).ToString();
        }
    }

    public void Observe(string stamp)
    {
        var value = Parse(stamp);
        lock (_gate)
        {
            if (value.PhysicalMilliseconds > _physicalMilliseconds)
            {
                _physicalMilliseconds = value.PhysicalMilliseconds;
                _logicalCounter = value.LogicalCounter;
            }
            else if (value.PhysicalMilliseconds == _physicalMilliseconds)
            {
                _logicalCounter = Math.Max(_logicalCounter, value.LogicalCounter);
            }
        }
    }

    public static HlcValue Parse(string stamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stamp);
        var parts = stamp.Split(':');
        if (parts.Length != 3 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var physical) ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var counter) ||
            physical < 0 || counter < 0 ||
            !Guid.TryParseExact(parts[2], "D", out var deviceId) ||
            parts[2] != parts[2].ToLowerInvariant() ||
            physical.ToString(CultureInfo.InvariantCulture) != parts[0] ||
            counter.ToString(CultureInfo.InvariantCulture) != parts[1])
        {
            throw new FormatException($"'{stamp}' is not a canonical HLC stamp.");
        }

        return new HlcValue(physical, counter, deviceId);
    }

    public static int Compare(string first, string second)
    {
        var left = Parse(first);
        var right = Parse(second);
        var physical = left.PhysicalMilliseconds.CompareTo(right.PhysicalMilliseconds);
        if (physical != 0)
        {
            return physical;
        }

        var logical = left.LogicalCounter.CompareTo(right.LogicalCounter);
        return logical != 0
            ? logical
            : string.Compare(
                left.DeviceId.ToString("D").ToLowerInvariant(),
                right.DeviceId.ToString("D").ToLowerInvariant(),
                StringComparison.Ordinal);
    }
}
