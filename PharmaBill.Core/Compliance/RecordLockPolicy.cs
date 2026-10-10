using System;

namespace PharmaBill.Core.Compliance;

public static class RecordLockPolicy
{
	public const int LockAfterDays = 5;

	public const string LockedMessage =
		"Record Locked (Older than 5 Days):\nUnder pharmacy audit regulations, records older than 5 days cannot be altered without Master Admin authorization and a verified compliance reason.";

	public const string EditableBadge = "Active / Editable";

	public const string LockedToolTip = "Locked & Immutable (> 5 days). Admin authorization required to modify.";

	public static bool IsLocked(DateTime createdAtUtc)
	{
		DateTime createdLocal = createdAtUtc.Kind switch
		{
			DateTimeKind.Utc => createdAtUtc.ToLocalTime(),
			DateTimeKind.Local => createdAtUtc,
			_ => DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc).ToLocalTime()
		};
		return (DateTime.Now - createdLocal).TotalDays > LockAfterDays;
	}
}

public sealed class RecordLockedException : InvalidOperationException
{
	public RecordLockedException(string entityType, Guid entityId, string action)
		: base(RecordLockPolicy.LockedMessage)
	{
		EntityType = entityType;
		EntityId = entityId;
		PendingAction = action;
	}

	public string EntityType { get; }

	public Guid EntityId { get; }

	public string PendingAction { get; }
}
