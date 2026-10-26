
namespace PharmaBill.Core.Compliance;

public static class RecordLockPolicy
{
	public const int LockAfterDays = 5;

	public const string LockedMessage =
		"Record Locked (Older than 5 Days):\nUnder pharmacy audit regulations, records older than 5 days cannot be altered without Master Admin authorization and a verified compliance reason.";

	public const string EditableBadge = "Active / Editable";

	public const string LockedToolTip = "Locked & Immutable (> 5 days). Admin authorization required to modify.";

	/// <summary>Elapsed time is measured between instants, so it does not depend on any timezone.</summary>
	public static bool IsLocked(DateTime createdAtUtc)
	{
		DateTime created = createdAtUtc.Kind == DateTimeKind.Local ? createdAtUtc.ToUniversalTime() : DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc);
		return (BusinessClock.UtcNow - created).TotalDays > LockAfterDays;
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
