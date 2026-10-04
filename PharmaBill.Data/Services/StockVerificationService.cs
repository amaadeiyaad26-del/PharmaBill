using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class StockVerificationService(
    IUnitOfWork unitOfWork,
    IEntitlementService entitlementService)
{
    public async Task<StockVerificationSession> StartAsync(
        string sessionNo,
        Guid userId,
        UserRole role,
        CancellationToken cancellationToken = default)
    {
        EnsureCanCount(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionNo);
        if (!await entitlementService.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken))
        {
            throw new InvalidOperationException("Stock verification is unavailable in read-only mode.");
        }

        if (await unitOfWork.Context.StockVerificationSessions.AnyAsync(
                session => session.SessionNo == sessionNo.Trim(),
                cancellationToken))
        {
            throw new InvalidOperationException("A stock verification session with this number already exists.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var session = new StockVerificationSession
        {
            SessionNo = sessionNo.Trim(),
            Status = "Open"
        };
        unitOfWork.Context.StockVerificationSessions.Add(session);
        foreach (var batch in await unitOfWork.Context.Batches.ToListAsync(cancellationToken))
        {
            var expected = await unitOfWork.Context.StockMovements
                .Where(movement => movement.BatchId == batch.Id)
                .SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken) ?? 0m;
            unitOfWork.Context.StockVerificationItems.Add(new StockVerificationItem
            {
                SessionId = session.Id,
                BatchId = batch.Id,
                ExpectedQuantity = expected,
                CountedQuantity = expected,
                AdjustmentQuantity = 0m
            });
        }

        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "StockVerificationStarted",
            EntityName = nameof(StockVerificationSession),
            EntityId = session.Id,
            Details = $"Session {session.SessionNo}"
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return session;
    }

    public async Task SetCountedQuantityAsync(
        Guid sessionId,
        Guid batchId,
        decimal countedQuantity,
        CancellationToken cancellationToken = default)
    {
        if (countedQuantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(countedQuantity));
        }

        var session = await unitOfWork.Context.StockVerificationSessions
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Stock verification session not found.");
        if (session.Status != "Open")
        {
            throw new InvalidOperationException("Only open stock verification sessions can be edited.");
        }

        var item = await unitOfWork.Context.StockVerificationItems
            .SingleOrDefaultAsync(row => row.SessionId == sessionId && row.BatchId == batchId, cancellationToken)
            ?? throw new InvalidOperationException("The batch is not part of this stock verification session.");
        item.CountedQuantity = countedQuantity;
        item.AdjustmentQuantity = countedQuantity - item.ExpectedQuantity;
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task PostAsync(
        Guid sessionId,
        Guid userId,
        UserRole role,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCanPost(role);
        if (!await entitlementService.CanPerformAsync(ProtectedOperation.EnterStock, cancellationToken))
        {
            throw new InvalidOperationException("Stock verification is unavailable in read-only mode.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var session = await unitOfWork.Context.StockVerificationSessions
            .SingleOrDefaultAsync(item => item.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Stock verification session not found.");
        if (session.Status != "Open")
        {
            throw new InvalidOperationException("This stock verification session has already been posted.");
        }

        var items = await unitOfWork.Context.StockVerificationItems
            .Where(item => item.SessionId == sessionId)
            .ToListAsync(cancellationToken);
        foreach (var item in items.Where(item => item.AdjustmentQuantity != 0))
        {
            var currentQuantity = await unitOfWork.Context.StockMovements
                .Where(movement => movement.BatchId == item.BatchId)
                .SumAsync(movement => (decimal?)movement.QuantityChange, cancellationToken) ?? 0m;
            if (currentQuantity != item.ExpectedQuantity)
            {
                throw new InvalidOperationException(
                    "Stock changed after this verification began. Start a new verification before posting.");
            }

            var batch = await unitOfWork.Context.Batches
                .SingleAsync(row => row.Id == item.BatchId, cancellationToken);
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
                ReferenceType = nameof(StockVerificationSession),
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
            EntityName = nameof(StockVerificationSession),
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
        if (!PermissionMatrix.Allows(role, AppPermission.EnterStock) ||
            role is not (UserRole.Owner or UserRole.Manager))
        {
            throw new UnauthorizedAccessException("Only the Owner or a Manager can post a physical count.");
        }
    }

    private static string PostingReason(string sessionNo, string? reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? $"Physical verification {sessionNo}"
            : $"Physical verification {sessionNo}: {reason.Trim()}"; 
}
