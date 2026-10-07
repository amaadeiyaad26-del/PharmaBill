using System;

namespace PharmaBill.Sync;

public sealed record CloudSyncSettings(bool Enabled, string ServerUrl, string ApiKey, string Status, string PullCursor, DateTime PushCursorUtc, DateTime? LastPushAtUtc, DateTime? LastPullAtUtc, int LastPushedChanges, int LastPulledChanges, string? LastError)
{
	public static CloudSyncSettings Default { get; } = new CloudSyncSettings(Enabled: false, string.Empty, string.Empty, "Idle", string.Empty, DateTime.MinValue, null, null, 0, 0, null);

	public bool IsConfigured
	{
		get
		{
			Guid branchId;
			if (Uri.TryCreate(ServerUrl, UriKind.Absolute, out Uri result) && (result.Scheme == Uri.UriSchemeHttps || result.IsLoopback))
			{
				return TryReadBranchId(out branchId);
			}
			return false;
		}
	}

	public bool TryReadBranchId(out Guid branchId)
	{
		branchId = Guid.Empty;
		string[] array = ApiKey.Split('_', 3);
		if (array.Length == 3 && array[0] == "pbk")
		{
			return Guid.TryParseExact(array[1], "N", out branchId);
		}
		return false;
	}
}
