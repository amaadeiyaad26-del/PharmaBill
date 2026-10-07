using System;
using PharmaBill.Core.Sync;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class HybridLogicalClock(HybridLogicalClockState state, DatabaseDeviceId deviceId)
{
	public string Now()
	{
		return state.Now(deviceId.Value, DateTime.UtcNow);
	}

	public string Receive(string remoteStamp)
	{
		return state.Receive(remoteStamp, deviceId.Value, DateTime.UtcNow);
	}

	public void Observe(string stamp)
	{
		state.Observe(stamp);
	}

	public static int Compare(string first, string second)
	{
		return HybridLogicalClockState.Compare(first, second);
	}
}
