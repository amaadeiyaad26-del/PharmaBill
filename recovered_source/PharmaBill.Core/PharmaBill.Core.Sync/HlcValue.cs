using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PharmaBill.Core.Sync;

public readonly record struct HlcValue(long PhysicalMilliseconds, long LogicalCounter, Guid DeviceId)
{
	public override string ToString()
	{
		IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
		DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(2, 3, invariantCulture);
		handler.AppendFormatted(PhysicalMilliseconds);
		handler.AppendLiteral(":");
		handler.AppendFormatted(LogicalCounter);
		handler.AppendLiteral(":");
		handler.AppendFormatted(DeviceId, "D");
		return string.Create(invariantCulture, ref handler).ToLowerInvariant();
	}
}
