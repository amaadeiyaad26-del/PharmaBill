using System.Collections.Generic;

namespace PharmaBill.App.Services;

public sealed record ProfileSummary(string DisplayName, string Role, string UserName, string Phone, string Email, string PharmacyName, string Address, string PharmacyPhone, string CompetentPerson, IReadOnlyList<string> Licences);
