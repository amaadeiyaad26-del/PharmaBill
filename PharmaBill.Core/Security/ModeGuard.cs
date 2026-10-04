using PharmaBill.Core.Entities;

namespace PharmaBill.Core.Security;

public static class ModeGuard
{
    public static IReadOnlyList<string> GetMissingLicenceTypes(
        BusinessMode mode,
        IEnumerable<LicenceRecord> licences,
        DateOnly today)
    {
        var validTypes = licences
            .Where(licence => !string.IsNullOrWhiteSpace(licence.LicenceNumber) &&
                              licence.ExpiresOn.HasValue &&
                              licence.ExpiresOn.Value >= today)
            .Select(licence => licence.LicenceType.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        if (mode is BusinessMode.Retail or BusinessMode.Both &&
            !validTypes.Overlaps(["20", "21"]))
        {
            missing.Add("Retail licence (type 20 or 21)");
        }

        if (mode is BusinessMode.Wholesaler or BusinessMode.Both &&
            !validTypes.Any(type => type is not "20" and not "21"))
        {
            missing.Add("Wholesale licence");
        }

        return missing;
    }

    public static bool CanSwitch(
        BusinessMode mode,
        IEnumerable<LicenceRecord> licences,
        DateOnly today) =>
        GetMissingLicenceTypes(mode, licences, today).Count == 0;
}
