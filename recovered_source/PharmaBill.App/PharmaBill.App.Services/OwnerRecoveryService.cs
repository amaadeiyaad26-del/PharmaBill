using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class OwnerRecoveryService(IServiceScopeFactory scopeFactory, RecoveryCodeStore recoveryCodeStore)
{
	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public async Task<bool> ResetOwnerPasswordAsync(string username, string recoveryCode, string newPassword, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(username, "username");
		ArgumentException.ThrowIfNullOrWhiteSpace(newPassword, "newPassword");
		if (newPassword.Length < 6)
		{
			throw new ArgumentException("PIN or password must contain at least 6 characters.", "newPassword");
		}
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!(await recoveryCodeStore.VerifyAsync(recoveryCode, cancellationToken)))
			{
				return false;
			}
			using IServiceScope scope = scopeFactory.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			AppUser owner = await context.AppUsers.SingleOrDefaultAsync((AppUser user) => (int)user.Role == 0 && user.IsActive && user.UserName == username, cancellationToken);
			if (owner == null)
			{
				return false;
			}
			bool result;
			await using (IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken))
			{
				owner.PasswordHash = PasswordHasher.Hash(newPassword);
				context.AuditLogs.Add(new AuditLog
				{
					UserId = owner.Id,
					ActionAtUtc = DateTime.UtcNow,
					Action = "OwnerPasswordResetUsingRecoveryCode",
					EntityName = "AppUser",
					EntityId = owner.Id
				});
				await context.SaveChangesAsync(cancellationToken);
				await transaction.CommitAsync(cancellationToken);
				await recoveryCodeStore.DeleteAsync(cancellationToken);
				result = true;
			}
			return result;
		}
		finally
		{
			_gate.Release();
		}
	}
}
