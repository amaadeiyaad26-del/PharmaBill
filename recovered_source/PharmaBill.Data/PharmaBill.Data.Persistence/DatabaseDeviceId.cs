using System;

namespace PharmaBill.Data.Persistence;

public sealed class DatabaseDeviceId
{
	public Guid Value { get; }

	public DatabaseDeviceId(Guid value)
	{
		Value = value;
	}
}
