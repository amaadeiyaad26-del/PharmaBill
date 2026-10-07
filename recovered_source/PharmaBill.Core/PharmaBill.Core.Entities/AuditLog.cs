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
}
