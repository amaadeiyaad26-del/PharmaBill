using System;

namespace PharmaBill.Sync;

public sealed class CloudSyncAuthenticationException : Exception
{
	public CloudSyncAuthenticationException(string message)
		: base(message)
	{
	}
}
