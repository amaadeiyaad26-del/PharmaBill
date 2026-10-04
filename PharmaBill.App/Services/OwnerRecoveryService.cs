using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class OwnerRecoveryService(
    IServiceScopeFactory scopeFactory,
    RecoveryCodeStore recoveryCodeStore)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<bool> ResetOwnerPasswordAsync(
        string username,
        string recoveryCode,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);
        if (newPassword.Length < 6)
        {
            throw new ArgumentException("PIN or password must contain at least 6 characters.", nameof(newPassword));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!await recoveryCodeStore.VerifyAsync(recoveryCode, cancellationToken))
            {
                return false;
            }

            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
            var owner = await context.AppUsers.SingleOrDefaultAsync(
                user => user.Role == UserRole.Owner && user.IsActive && user.UserName == username,
                cancellationToken);
            if (owner is null)
            {
                return false;
            }

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            owner.PasswordHash = PasswordHasher.Hash(newPassword);
            context.AuditLogs.Add(new AuditLog
            {
                UserId = owner.Id,
                ActionAtUtc = DateTime.UtcNow,
                Action = "OwnerPasswordResetUsingRecoveryCode",
                EntityName = nameof(AppUser),
                EntityId = owner.Id
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await recoveryCodeStore.DeleteAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
