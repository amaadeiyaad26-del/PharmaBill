using System;
using System.Globalization;

namespace PharmaBill.Core.Sync;

public sealed class HybridLogicalClockState
{
	private readonly object _gate = new object();

	private long _physicalMilliseconds;

	private long _logicalCounter;

	public string Now(Guid deviceId, DateTime utcNow)
	{
		if (utcNow.Kind != DateTimeKind.Utc)
		{
			throw new ArgumentException("HLC wall time must be UTC.", "utcNow");
		}
		long num = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
		lock (_gate)
		{
			if (num > _physicalMilliseconds)
			{
				_physicalMilliseconds = num;
				_logicalCounter = 0L;
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
			throw new ArgumentException("HLC wall time must be UTC.", "utcNow");
		}
		HlcValue hlcValue = Parse(remoteStamp);
		long val = new DateTimeOffset(utcNow).ToUnixTimeMilliseconds();
		lock (_gate)
		{
			long num = Math.Max(val, Math.Max(_physicalMilliseconds, hlcValue.PhysicalMilliseconds));
			if (num == _physicalMilliseconds && num == hlcValue.PhysicalMilliseconds)
			{
				_logicalCounter = Math.Max(_logicalCounter, hlcValue.LogicalCounter) + 1;
			}
			else if (num == _physicalMilliseconds)
			{
				_logicalCounter++;
			}
			else if (num == hlcValue.PhysicalMilliseconds)
			{
				_logicalCounter = hlcValue.LogicalCounter + 1;
			}
			else
			{
				_logicalCounter = 0L;
			}
			_physicalMilliseconds = num;
			return new HlcValue(_physicalMilliseconds, _logicalCounter, localDeviceId).ToString();
		}
	}

	public void Observe(string stamp)
	{
		HlcValue hlcValue = Parse(stamp);
		lock (_gate)
		{
			if (hlcValue.PhysicalMilliseconds > _physicalMilliseconds)
			{
				_physicalMilliseconds = hlcValue.PhysicalMilliseconds;
				_logicalCounter = hlcValue.LogicalCounter;
			}
			else if (hlcValue.PhysicalMilliseconds == _physicalMilliseconds)
			{
				_logicalCounter = Math.Max(_logicalCounter, hlcValue.LogicalCounter);
			}
		}
	}

	public static HlcValue Parse(string stamp)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(stamp, "stamp");
		string[] array = stamp.Split(':');
		if (array.Length != 3 || !long.TryParse(array[0], NumberStyles.None, CultureInfo.InvariantCulture, out var result) || !long.TryParse(array[1], NumberStyles.None, CultureInfo.InvariantCulture, out var result2) || result < 0 || result2 < 0 || !Guid.TryParseExact(array[2], "D", out var result3) || array[2] != array[2].ToLowerInvariant() || result.ToString(CultureInfo.InvariantCulture) != array[0] || result2.ToString(CultureInfo.InvariantCulture) != array[1])
		{
			throw new FormatException("'" + stamp + "' is not a canonical HLC stamp.");
		}
		return new HlcValue(result, result2, result3);
	}

	public static int Compare(string first, string second)
	{
		HlcValue hlcValue = Parse(first);
		HlcValue hlcValue2 = Parse(second);
		int num = hlcValue.PhysicalMilliseconds.CompareTo(hlcValue2.PhysicalMilliseconds);
		if (num != 0)
		{
			return num;
		}
		int num2 = hlcValue.LogicalCounter.CompareTo(hlcValue2.LogicalCounter);
		if (num2 == 0)
		{
			return string.Compare(hlcValue.DeviceId.ToString("D").ToLowerInvariant(), hlcValue2.DeviceId.ToString("D").ToLowerInvariant(), StringComparison.Ordinal);
		}
		return num2;
	}
}
