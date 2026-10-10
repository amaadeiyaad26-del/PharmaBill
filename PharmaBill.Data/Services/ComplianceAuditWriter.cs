using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record ComplianceAuditEntry
{
	public required string EntityType { get; init; }

	public required Guid EntityId { get; init; }

	public required string Action { get; init; }

	public required string AuthorizedBy { get; init; }

	public required string Reason { get; init; }

	public required string OldSnapshotJson { get; init; }

	public required string NewSnapshotJson { get; init; }

	public Guid? UserId { get; init; }
}

public static class RecordLockGuard
{
	public static void Demand(DateTime createdAtUtc, string entityType, Guid entityId, string action)
	{
		if (!RecordLockPolicy.IsLocked(createdAtUtc))
		{
			return;
		}

		RecordUnlockGrant? grant = RecordUnlockContext.Current;
		if (grant == null || !grant.Covers(entityType))
		{
			throw new RecordLockedException(entityType, entityId, action);
		}
	}
}

/// <summary>
/// Append-only hash chain for AuditLogs. There is no delete API; PharmaBillDbContext rejects row deletion.
/// </summary>
public static class ComplianceAuditWriter
{
	public static string Snapshot(object value)
	{
		return JsonSerializer.Serialize(value);
	}

	public static async Task AppendAsync(PharmaBillDbContext context, ComplianceAuditEntry entry, CancellationToken cancellationToken = default)
	{
		string previous = context.ChangeTracker.Entries<AuditLog>()
			.Where(item => item.State == EntityState.Added && !string.IsNullOrEmpty(item.Entity.RecordHash))
			.OrderByDescending(item => item.Entity.Timestamp ?? item.Entity.ActionAtUtc)
			.Select(item => item.Entity.RecordHash)
			.FirstOrDefault()
			?? await context.AuditLogs.AsNoTracking()
				.OrderByDescending(item => item.ActionAtUtc)
				.Select(item => item.RecordHash)
				.FirstOrDefaultAsync(cancellationToken)
			?? string.Empty;

		DateTime timestamp = DateTime.UtcNow;
		string hash = ComputeHash(previous, entry, timestamp);
		context.AuditLogs.Add(new AuditLog
		{
			UserId = entry.UserId,
			ActionAtUtc = timestamp,
			Timestamp = timestamp,
			Action = entry.Action,
			EntityName = entry.EntityType,
			EntityType = entry.EntityType,
			EntityId = entry.EntityId,
			Details = entry.Reason,
			AuthorizedBy = entry.AuthorizedBy,
			Reason = entry.Reason,
			OldSnapshotJson = entry.OldSnapshotJson,
			NewSnapshotJson = entry.NewSnapshotJson,
			RecordHash = hash
		});
	}

	public static async Task AppendIfUnlockedAsync(
		PharmaBillDbContext context,
		DateTime createdAtUtc,
		ComplianceAuditEntry entry,
		CancellationToken cancellationToken = default)
	{
		if (!RecordLockPolicy.IsLocked(createdAtUtc))
		{
			return;
		}

		RecordUnlockGrant grant = RecordUnlockContext.Current ?? throw new RecordLockedException(entry.EntityType, entry.EntityId, entry.Action);
		if (!grant.Covers(entry.EntityType))
		{
			throw new RecordLockedException(entry.EntityType, entry.EntityId, entry.Action);
		}

		await AppendAsync(context, entry with
		{
			AuthorizedBy = grant.AdminUserName,
			Reason = grant.Reason,
			UserId = grant.AdminUserId,
			Action = string.IsNullOrWhiteSpace(entry.Action) ? grant.Action : entry.Action
		}, cancellationToken);
	}

	private static string ComputeHash(string previousHash, ComplianceAuditEntry entry, DateTime timestamp)
	{
		string payload = string.Join("|", previousHash, timestamp.ToString("O"), entry.EntityType, entry.EntityId, entry.Action, entry.AuthorizedBy, entry.Reason, entry.OldSnapshotJson, entry.NewSnapshotJson);
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
		return Convert.ToHexString(hash);
	}
}
