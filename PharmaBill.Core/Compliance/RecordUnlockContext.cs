using System;
using System.Threading;

namespace PharmaBill.Core.Compliance;

public static class RecordUnlockContext
{
	private static readonly AsyncLocal<RecordUnlockGrant?> CurrentGrant = new();

	public static RecordUnlockGrant? Current => CurrentGrant.Value;

	public static void Arm(RecordUnlockGrant grant)
	{
		CurrentGrant.Value = grant;
	}

	public static void Clear()
	{
		CurrentGrant.Value = null;
	}

	public static IDisposable Use(RecordUnlockGrant grant)
	{
		RecordUnlockGrant? prior = CurrentGrant.Value;
		CurrentGrant.Value = grant;
		return new Scope(prior);
	}

	private sealed class Scope(RecordUnlockGrant? prior) : IDisposable
	{
		public void Dispose()
		{
			CurrentGrant.Value = prior;
		}
	}
}
