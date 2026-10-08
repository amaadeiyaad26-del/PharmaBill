using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Compliance;
using PharmaBill.Core.Entities;

namespace PharmaBill.App.Services;

public sealed class RecordComplianceGate(SensitiveAccessService access)
{
	public async Task<RecordUnlockGrant?> RequestAsync(Window? owner, string entityType, Guid entityId, string action, CancellationToken cancellationToken = default)
	{
		RecordUnlockWindow window = new();
		if (owner != null)
		{
			window.Owner = owner;
		}

		if (window.ShowDialog() != true)
		{
			return null;
		}

		AppUser? admin = await access.AuthenticateAdminAsync(window.AdminUserName, window.Pin, cancellationToken);
		if (admin == null)
		{
			MessageBox.Show(owner, "Master Admin authorization was denied.", "Record Locked (Older than 5 Days)", MessageBoxButton.OK, MessageBoxImage.Warning);
			return null;
		}

		return new RecordUnlockGrant
		{
			AdminUserId = admin.Id,
			AdminUserName = admin.UserName,
			AdminRole = admin.Role,
			Reason = window.ReasonCode + ": " + window.Notes,
			EntityType = entityType,
			EntityId = entityId,
			Action = action
		};
	}
}

public static class RecordLockUi
{
	public static async Task RunAsync(IServiceProvider services, Func<Task> action)
	{
		RecordComplianceGate gate = services.GetRequiredService<RecordComplianceGate>();
		Window? owner = Application.Current?.MainWindow;
		RecordUnlockGrant? armed = null;
		try
		{
			for (int attempt = 0; attempt < 4; attempt++)
			{
				if (armed != null)
				{
					RecordUnlockContext.Arm(armed);
				}

				try
				{
					await action();
					return;
				}
				catch (RecordLockedException ex)
				{
					RecordUnlockContext.Clear();
					armed = await gate.RequestAsync(owner, ex.EntityType, ex.EntityId, ex.PendingAction);
					if (armed == null)
					{
						throw new InvalidOperationException(RecordLockPolicy.LockedMessage);
					}
				}
			}

			throw new InvalidOperationException(RecordLockPolicy.LockedMessage);
		}
		finally
		{
			RecordUnlockContext.Clear();
		}
	}
}
