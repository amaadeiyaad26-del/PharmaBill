using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class StockVerificationService(IUnitOfWork unitOfWork, IEntitlementService entitlementService)
{
	public async Task<StockVerificationSession> StartAsync(string sessionNo, Guid userId, UserRole role, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureCanCount(role);
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionNo, "sessionNo");
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Stock verification is unavailable in read-only mode.");
		}
		if (await unitOfWork.Context.StockVerificationSessions.AnyAsync((StockVerificationSession stockVerificationSession) => stockVerificationSession.SessionNo == sessionNo.Trim(), cancellationToken))
		{
			throw new InvalidOperationException("A stock verification session with this number already exists.");
		}
		StockVerificationSession result;
		await using (IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
		{
			StockVerificationSession session = new StockVerificationSession
			{
				SessionNo = sessionNo.Trim(),
				Status = "Open"
			};
			unitOfWork.Context.StockVerificationSessions.Add(session);
			foreach (Batch batch in await unitOfWork.Context.Batches.ToListAsync(cancellationToken))
			{
				decimal valueOrDefault = (await unitOfWork.Context.StockMovements.Where((StockMovement movement) => movement.BatchId == batch.Id).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault();
				unitOfWork.Context.StockVerificationItems.Add(new StockVerificationItem
				{
					SessionId = session.Id,
					BatchId = batch.Id,
					ExpectedQuantity = valueOrDefault,
					CountedQuantity = valueOrDefault,
					AdjustmentQuantity = 0m
				});
			}
			unitOfWork.Context.AuditLogs.Add(new AuditLog
			{
				UserId = userId,
				Action = "StockVerificationStarted",
				EntityName = "StockVerificationSession",
				EntityId = session.Id,
				Details = "Session " + session.SessionNo
			});
			await unitOfWork.SaveChangesAsync(cancellationToken);
			await transaction.CommitAsync(cancellationToken);
			result = session;
		}
		return result;
	}

	public async Task SetCountedQuantityAsync(Guid sessionId, Guid batchId, decimal countedQuantity, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (countedQuantity < 0m)
		{
			throw new ArgumentOutOfRangeException("countedQuantity");
		}
		if (((await unitOfWork.Context.StockVerificationSessions.SingleOrDefaultAsync((StockVerificationSession item) => item.Id == sessionId, cancellationToken)) ?? throw new InvalidOperationException("Stock verification session not found.")).Status != "Open")
		{
			throw new InvalidOperationException("Only open stock verification sessions can be edited.");
		}
		StockVerificationItem stockVerificationItem = (await unitOfWork.Context.StockVerificationItems.SingleOrDefaultAsync((StockVerificationItem row) => row.SessionId == sessionId && row.BatchId == batchId, cancellationToken)) ?? throw new InvalidOperationException("The batch is not part of this stock verification session.");
		stockVerificationItem.CountedQuantity = countedQuantity;
		stockVerificationItem.AdjustmentQuantity = countedQuantity - stockVerificationItem.ExpectedQuantity;
		await unitOfWork.SaveChangesAsync(cancellationToken);
	}

	public async Task PostAsync(Guid sessionId, Guid userId, UserRole role, string? reason = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureCanPost(role);
		if (!(await entitlementService.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken)))
		{
			throw new InvalidOperationException("Stock verification is unavailable in read-only mode.");
		}
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		StockVerificationSession session = (await unitOfWork.Context.StockVerificationSessions.SingleOrDefaultAsync((StockVerificationSession stockVerificationSession) => stockVerificationSession.Id == sessionId, cancellationToken)) ?? throw new InvalidOperationException("Stock verification session not found.");
		if (session.Status != "Open")
		{
			throw new InvalidOperationException("This stock verification session has already been posted.");
		}
		foreach (StockVerificationItem item in (await unitOfWork.Context.StockVerificationItems.Where((StockVerificationItem stockVerificationItem) => stockVerificationItem.SessionId == sessionId).ToListAsync(cancellationToken)).Where((StockVerificationItem stockVerificationItem) => stockVerificationItem.AdjustmentQuantity != 0m))
		{
			if ((await unitOfWork.Context.StockMovements.Where((StockMovement movement) => movement.BatchId == item.BatchId).SumAsync((Expression<Func<StockMovement, decimal?>>)((StockMovement movement) => movement.QuantityChange), cancellationToken)).GetValueOrDefault() != item.ExpectedQuantity)
			{
				throw new InvalidOperationException("Stock changed after this verification began. Start a new verification before posting.");
			}
			Batch batch = await unitOfWork.Context.Batches.SingleAsync((Batch row) => row.Id == item.BatchId, cancellationToken);
			batch.Quantity = item.CountedQuantity;
			unitOfWork.Context.StockAdjustments.Add(new StockAdjustment
			{
				BatchId = batch.Id,
				DrugId = batch.DrugId,
				QuantityChange = item.AdjustmentQuantity,
				Reason = PostingReason(session.SessionNo, reason)
			});
			unitOfWork.Context.StockMovements.Add(new StockMovement
			{
				BatchId = batch.Id,
				DrugId = batch.DrugId,
				QuantityChange = item.AdjustmentQuantity,
				MovementType = "PhysicalVerification",
				ReferenceType = "StockVerificationSession",
				ReferenceId = session.Id,
				Notes = PostingReason(session.SessionNo, reason)
			});
		}
		session.Status = "Posted";
		session.PostedAtUtc = DateTime.UtcNow;
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = userId,
			Action = "StockVerificationPosted",
			EntityName = "StockVerificationSession",
			EntityId = session.Id,
			Details = PostingReason(session.SessionNo, reason)
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}

	private static void EnsureCanCount(UserRole role)
	{
		if (!PermissionMatrix.Allows(role, AppPermission.EnterStock))
		{
			throw new UnauthorizedAccessException("Your role cannot manage stock verification.");
		}
	}

	private static void EnsureCanPost(UserRole role)
	{
		bool flag = !PermissionMatrix.Allows(role, AppPermission.EnterStock);
		if (!flag)
		{
			bool flag2 = (uint)role <= 1u;
			flag = !flag2;
		}
		if (flag)
		{
			throw new UnauthorizedAccessException("Only the Owner or a Manager can post a physical count.");
		}
	}

	private static string PostingReason(string sessionNo, string? reason)
	{
		if (!string.IsNullOrWhiteSpace(reason))
		{
			return "Physical verification " + sessionNo + ": " + reason.Trim();
		}
		return "Physical verification " + sessionNo;
	}
}
