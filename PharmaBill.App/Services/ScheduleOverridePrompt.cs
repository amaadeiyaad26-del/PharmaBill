using System.Windows;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>Asks for a schedule override: the reason, then an Admin's username and PIN. Null when either is not given.</summary>
public interface IScheduleOverridePrompt
{
	Task<ScheduleChangeAuthorization?> RequestAsync(ScheduleChangeRequiredException block, CancellationToken cancellationToken = default);
}

public sealed class ScheduleOverridePrompt(SensitiveAccessService access) : IScheduleOverridePrompt
{
	public async Task<ScheduleChangeAuthorization?> RequestAsync(ScheduleChangeRequiredException block, CancellationToken cancellationToken = default)
	{
		Window? owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive) ?? Application.Current?.MainWindow;
		var dialog = new ScheduleOverrideWindow(block);
		if (owner != null && !ReferenceEquals(owner, dialog))
		{
			dialog.Owner = owner;
		}
		if (dialog.ShowDialog() != true)
		{
			return null;
		}
		// The app's Admin PIN prompt: three attempts, each grant or refusal written to the audit log.
		(bool success, AppUser? admin) = await access.RequestAdminOverrideAsync(owner, $"change the schedule of {block.MedicineName} from {block.SavedSchedule} to {block.RequestedSchedule}", cancellationToken);
		return success && admin != null ? new ScheduleChangeAuthorization(admin.Id, dialog.Reason) : null;
	}
}

/// <summary>
/// Stock entry with the schedule override in the loop: a schedule change asks for the override and, once an Admin has
/// authorized it, saves again with that authorization. Declined or a wrong PIN: nothing is saved.
/// </summary>
public static class ScheduleOverrideFlow
{
	public static async Task<AddStockResult> AddStockAsync(AddStockService service, AddStockInput input, AppUser user, IScheduleOverridePrompt? prompt, CancellationToken cancellationToken = default)
	{
		try
		{
			return await service.AddStockAsync(input, user.Id, user.Role, cancellationToken);
		}
		catch (ScheduleChangeRequiredException block) when (prompt != null)
		{
			ScheduleChangeAuthorization? authorization = await prompt.RequestAsync(block, cancellationToken);
			if (authorization == null)
			{
				throw new ScheduleOverrideDeclinedException(block);
			}
			return await service.AddStockAsync(input with { ScheduleChange = authorization }, user.Id, user.Role, cancellationToken);
		}
	}
}

/// <summary>The schedule override was cancelled or its PIN not verified: the stock entry stays on screen, unsaved.</summary>
public sealed class ScheduleOverrideDeclinedException(ScheduleChangeRequiredException block)
	: InvalidOperationException($"Stock was not added: the schedule override was not authorized. {block.MedicineName} stays schedule {block.SavedSchedule}; choose {block.SavedSchedule} to add this stock, or try the override again.")
{
	public ScheduleChangeRequiredException Block { get; } = block;
}
