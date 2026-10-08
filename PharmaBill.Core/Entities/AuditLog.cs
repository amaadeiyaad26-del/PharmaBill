using System;

namespace PharmaBill.Core.Entities;

public sealed class AuditLog : EntityBase
{
	public Guid? UserId { get; set; }

	public DateTime ActionAtUtc { get; set; } = DateTime.UtcNow;

	public string Action { get; set; } = string.Empty;

	public string EntityName { get; set; } = string.Empty;

	public Guid? EntityId { get; set; }

	public string? Details { get; set; }

	public Guid? BranchId { get; set; }

	public DateTime? Timestamp { get; set; }

	public string? EntityType { get; set; }

	public string? AuthorizedBy { get; set; }

	public string? Reason { get; set; }

	public string? OldSnapshotJson { get; set; }

	public string? NewSnapshotJson { get; set; }

	public string? RecordHash { get; set; }
}
