using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App.Services;

public sealed class SensitiveAccessService(
    IServiceScopeFactory scopeFactory,
    CurrentSession currentSession,
    DocumentOutputSettingsStore settingsStore)
{
    public async Task<bool> RequestCurrentUserPinAsync(
        Window? owner,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var prompt = new PinPromptWindow($"Enter your PIN or password to {purpose} (attempt {attempt} of 3).");
            if (owner is not null)
            {
                prompt.Owner = owner;
            }

            if (prompt.ShowDialog() != true)
            {
                return false;
            }

            if (await VerifyCurrentUserPinAsync(prompt.Pin, cancellationToken))
            {
                await RecordAsync("SensitiveRecordsAccessGranted", purpose, cancellationToken);
                return true;
            }

            await RecordAsync("SensitiveRecordsAccessDenied", purpose, cancellationToken);
        }

        return false;
    }

    public async Task<bool> RequestInspectorPinAsync(
        Window? owner,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var prompt = new PinPromptWindow($"Enter the separate inspector PIN (attempt {attempt} of 3).");
            if (owner is not null)
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

    public async Task<bool> VerifyCurrentUserPinAsync(
        string pin,
        CancellationToken cancellationToken = default)
    {
        if (currentSession.User is null)
        {
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
        var hash = await context.AppUsers.AsNoTracking()
            .Where(item => item.Id == currentSession.User.Id && item.IsActive)
            .Select(item => item.PasswordHash)
            .SingleOrDefaultAsync(cancellationToken);
        return hash is not null && PasswordHasher.Verify(pin, hash);
    }

    public bool VerifyInspectorPin(string pin) => settingsStore.VerifyInspectorPin(pin);

    public async Task RecordAsync(
        string action,
        string details,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        unitOfWork.Context.AuditLogs.Add(new AuditLog
        {
            UserId = currentSession.User?.Id,
            Action = action,
            EntityName = nameof(SensitiveAccessService),
            Details = details
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
