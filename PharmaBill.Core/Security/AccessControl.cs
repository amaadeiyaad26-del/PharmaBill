using PharmaBill.Core.Entities;

namespace PharmaBill.Core.Security;

public enum AppPermission
{
    View,
    Search,
    Print,
    Export,
    Backup,
    CreateBill,
    EnterStock,
    ProcessReturn,
    ManagePurchases,
    ManageCustomers,
    ManageUsers,
    ChangeBusinessMode,
    ManageSettings
}

public static class PermissionMatrix
{
    private static readonly IReadOnlyDictionary<UserRole, IReadOnlySet<AppPermission>> Permissions =
        new Dictionary<UserRole, IReadOnlySet<AppPermission>>
        {
            [UserRole.Owner] = Enum.GetValues<AppPermission>().ToHashSet(),
            [UserRole.Manager] = Enum.GetValues<AppPermission>().ToHashSet(),
            [UserRole.Pharmacist] = new HashSet<AppPermission>
            {
                AppPermission.View, AppPermission.Search, AppPermission.Print, AppPermission.Export,
                AppPermission.CreateBill, AppPermission.EnterStock, AppPermission.ProcessReturn,
                AppPermission.ManagePurchases, AppPermission.ManageCustomers
            },
            [UserRole.BillingClerk] = new HashSet<AppPermission>
            {
                AppPermission.View, AppPermission.Search, AppPermission.Print, AppPermission.Export,
                AppPermission.CreateBill, AppPermission.ManageCustomers
            },
            [UserRole.Salesman] = new HashSet<AppPermission>
            {
                AppPermission.View, AppPermission.Search, AppPermission.CreateBill, AppPermission.ManageCustomers
            },
            [UserRole.Accountant] = new HashSet<AppPermission>
            {
                AppPermission.View, AppPermission.Search, AppPermission.Print, AppPermission.Export,
                AppPermission.Backup
            }
        };

    public static bool Allows(UserRole role, AppPermission permission) =>
        Permissions.TryGetValue(role, out var permissions) && permissions.Contains(permission);

    public static bool AllowsInReadOnlyMode(AppPermission permission) =>
        permission is AppPermission.View or AppPermission.Search or AppPermission.Print or
            AppPermission.Export or AppPermission.Backup;
}

public enum EntitlementStatus
{
    TRIAL,
    SUBSCRIBED,
    GRACE_OFFLINE,
    EXPIRED
}

public enum ProtectedOperation
{
    View,
    Search,
    Print,
    Export,
    Backup,
    CreateBill,
    EnterStock,
    ProcessReturn
}

public interface IEntitlementService
{
    Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default);

    Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default);
}

public static class TrialClock
{
    public const int TRIAL_DAYS = 7;

    public static TimeSpan TrialDuration => TimeSpan.FromDays(TRIAL_DAYS);

    public static TrialEvaluation Evaluate(
        DateTime utcNow,
        IEnumerable<DateTime> persistedTrialStartsUtc,
        DateTime? lastObservedUtc)
    {
        var start = persistedTrialStartsUtc.DefaultIfEmpty(utcNow).Min();
        var movedBackwards = lastObservedUtc.HasValue && utcNow < lastObservedUtc.Value;
        var elapsed = movedBackwards ? TrialDuration : utcNow - start;
        var remaining = TrialDuration - elapsed;
        return new TrialEvaluation(
            start,
            movedBackwards,
            remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero,
            !movedBackwards && remaining > TimeSpan.Zero);
    }
}

public sealed record TrialEvaluation(
    DateTime TrialStartedAtUtc,
    bool ClockMovedBackwards,
    TimeSpan Remaining,
    bool IsTrialActive);
