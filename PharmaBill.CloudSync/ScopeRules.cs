using PharmaBill.CloudSync.Data;

namespace PharmaBill.CloudSync;

// Master data is shared by every branch of a tenant; everything else stays in its branch
// unless the pusher lists explicit branches (e.g. inter-branch stock transfers).
public static class ScopeRules
{
    private static readonly HashSet<string> GlobalEntities = new(StringComparer.Ordinal)
    {
        "Drug", "Supplier", "Customer", "CustomerLicence", "ScheduleOverride", "LicenceRecord"
    };

    public static bool IsGlobal(string entity) => GlobalEntities.Contains(entity);

    public static ChangeScope Classify(string entity, IReadOnlyCollection<Guid>? sharedWith) =>
        IsGlobal(entity) ? ChangeScope.Global
        : sharedWith is { Count: > 0 } ? ChangeScope.Shared
        : ChangeScope.Branch;
}
