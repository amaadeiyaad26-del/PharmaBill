using System;
using PharmaBill.Core.Entities;

namespace PharmaBill.Core.Compliance;

public sealed class RecordUnlockGrant
{
	public required Guid AdminUserId { get; init; }

	public required string AdminUserName { get; init; }

	public required UserRole AdminRole { get; init; }

	public required string Reason { get; init; }

	public required string EntityType { get; init; }

	public required Guid EntityId { get; init; }

	public required string Action { get; init; }

	public bool Covers(string entityType)
	{
		return string.Equals(EntityType, entityType, StringComparison.OrdinalIgnoreCase);
	}
}
