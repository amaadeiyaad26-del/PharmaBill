using System;

namespace PharmaBill.Sync;

public sealed class CloudOAuthNotConfiguredException : InvalidOperationException
{
	public string Provider { get; }

	public CloudOAuthNotConfiguredException(string provider, string message)
		: base(message)
	{
		Provider = provider;
	}
}
