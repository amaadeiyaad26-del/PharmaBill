using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class SensitiveAccessService(IServiceScopeFactory scopeFactory, CurrentSession currentSession, DocumentOutputSettingsStore settingsStore)
{
	public async Task<bool> RequestCurrentUserPinAsync(Window? owner, string purpose, CancellationToken cancellationToken = default(CancellationToken))
	{
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			PinPromptWindow pinPromptWindow = new PinPromptWindow($"Enter your PIN or password to {purpose} (attempt {attempt} of 3).");
			if (owner != null)
			{
				pinPromptWindow.Owner = owner;
			}
			if (pinPromptWindow.ShowDialog() != true)
			{
				return false;
			}
			if (await VerifyCurrentUserPinAsync(pinPromptWindow.Pin, cancellationToken))
			{
				await RecordAsync("SensitiveRecordsAccessGranted", purpose, cancellationToken);
				return true;
			}
			await RecordAsync("SensitiveRecordsAccessDenied", purpose, cancellationToken);
		}
		return false;
	}

	public async Task<bool> RequestInspectorPinAsync(Window? owner, CancellationToken cancellationToken = default(CancellationToken))
	{
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			PinPromptWindow prompt = new PinPromptWindow($"Enter the separate inspector PIN (attempt {attempt} of 3).");
			if (owner != null)
			{
				prompt.Owner = owner;
			}
			if (prompt.ShowDialog() != true)
			{
				return false;
			}
			if (await Task.Run(() => VerifyInspectorPin(prompt.Pin), cancellationToken))
			{
				await RecordAsync("InspectorViewAccessGranted", "Read-only inspector view opened.", cancellationToken);
				return true;
			}
			await RecordAsync("InspectorViewAccessDenied", "Invalid inspector PIN.", cancellationToken);
		}
		return false;
	}

	public async Task<bool> VerifyCurrentUserPinAsync(string pin, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (currentSession.User == null)
		{
			return false;
		}
		using IServiceScope scope = scopeFactory.CreateScope();
		string text = await (from item in scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>().AppUsers.AsNoTracking()
			where item.Id == currentSession.User.Id && item.IsActive
			select item.PasswordHash).SingleOrDefaultAsync(cancellationToken);
		return text != null && PasswordHasher.Verify(pin, text);
	}

	public async Task<(bool Success, AppUser? Admin)> RequestAdminOverrideAsync(Window? owner, string purpose, CancellationToken cancellationToken = default(CancellationToken))
	{
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			PinPromptWindow pinPromptWindow = new PinPromptWindow($"Admin override required to {purpose}.\nEnter an Admin username and PIN (attempt {attempt} of 3).");
			pinPromptWindow.ShowUsername = true;
			if (owner != null)
			{
				pinPromptWindow.Owner = owner;
			}
			if (pinPromptWindow.ShowDialog() != true)
			{
				return (Success: false, Admin: null);
			}
			AppUser admin = await AuthenticateAdminAsync(pinPromptWindow.Username, pinPromptWindow.Pin, cancellationToken);
			if (admin != null)
			{
				await RecordAsync("AdminOverrideGranted", $"{purpose} by {admin.UserName} ({PermissionMatrix.ToOperatorLabel(admin.Role)})", cancellationToken);
				return (Success: true, Admin: admin);
			}
			await RecordAsync("AdminOverrideDenied", purpose, cancellationToken);
		}
		return (Success: false, Admin: null);
	}

	private async Task<AppUser?> AuthenticateAdminAsync(string? username, string pin, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(pin))
		{
			return null;
		}
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		AppUser appUser;
		if (!string.IsNullOrWhiteSpace(username))
		{
			string normalized = username.Trim().ToLowerInvariant();
			appUser = await requiredService.AppUsers.AsNoTracking().FirstOrDefaultAsync((AppUser item) => item.IsActive && item.UserName.ToLower() == normalized, cancellationToken);
		}
		else
		{
			if (currentSession.User == null || !PermissionMatrix.Allows(currentSession.User.Role, AppPermission.EditPrintedBills))
			{
				return null;
			}
			appUser = await requiredService.AppUsers.AsNoTracking().FirstOrDefaultAsync((AppUser item) => item.Id == currentSession.User.Id && item.IsActive, cancellationToken);
		}
		if (appUser == null || !PermissionMatrix.Allows(appUser.Role, AppPermission.EditPrintedBills) || !PasswordHasher.Verify(pin, appUser.PasswordHash))
		{
			return null;
		}
		return appUser;
	}

	public bool VerifyInspectorPin(string pin)
	{
		return settingsStore.VerifyInspectorPin(pin);
	}

	public async Task RecordAsync(string action, string details, CancellationToken cancellationToken = default(CancellationToken))
	{
		using IServiceScope scope = scopeFactory.CreateScope();
		IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
		await using IUnitOfWorkTransaction transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
		unitOfWork.Context.AuditLogs.Add(new AuditLog
		{
			UserId = currentSession.User?.Id,
			Action = action,
			EntityName = "SensitiveAccessService",
			Details = details
		});
		await unitOfWork.SaveChangesAsync(cancellationToken);
		await transaction.CommitAsync(cancellationToken);
	}
}
