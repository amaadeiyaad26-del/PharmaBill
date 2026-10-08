using System;
using System.Collections.Generic;
using System.Linq;
using PharmaBill.Core.Entities;

namespace PharmaBill.Core.Security;

public static class ModeGuard
{
	public static IReadOnlyList<string> GetMissingLicenceTypes(BusinessMode mode, IEnumerable<LicenceRecord> licences, DateOnly today)
	{
		// Numbers without expiry still count (onboarding may defer dates to Settings).
		// Explicitly expired licences do not.
		HashSet<string> hashSet = (from licence in licences
			where !string.IsNullOrWhiteSpace(licence.LicenceNumber)
				&& (!licence.ExpiresOn.HasValue || licence.ExpiresOn.Value >= today)
			select licence.LicenceType.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
		List<string> list = new List<string>();
		bool flag = ((mode == BusinessMode.Retail || mode == BusinessMode.Both) ? true : false);
		if (flag && !hashSet.Overlaps(new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "20", "21" })))
		{
			list.Add("Retail licence (type 20 or 21)");
		}
		flag = (uint)(mode - 1) <= 1u;
		if (flag && !hashSet.Any((string type) => !(type == "20") && !(type == "21")))
		{
			list.Add("Wholesale licence");
		}
		return list;
	}

	public static bool CanSwitch(BusinessMode mode, IEnumerable<LicenceRecord> licences, DateOnly today)
	{
		return GetMissingLicenceTypes(mode, licences, today).Count == 0;
	}
}
