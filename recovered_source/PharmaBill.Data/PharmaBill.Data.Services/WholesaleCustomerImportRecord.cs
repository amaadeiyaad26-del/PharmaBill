using System.Collections.Generic;
using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Services;

public sealed record WholesaleCustomerImportRecord(Customer Customer, IReadOnlyList<CustomerLicence> Licences);
