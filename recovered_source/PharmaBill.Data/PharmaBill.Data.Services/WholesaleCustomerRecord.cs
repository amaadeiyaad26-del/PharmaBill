using System.Collections.Generic;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Wholesale;

namespace PharmaBill.Data.Services;

public sealed record WholesaleCustomerRecord(Customer Customer, IReadOnlyList<CustomerLicence> Licences, CustomerLicenceExpiryAlert ExpiryAlert)
{
	public string ExpiryAlertText => ExpiryAlert switch
	{
		CustomerLicenceExpiryAlert.Expired => "Licence expired", 
		CustomerLicenceExpiryAlert.ExpiringWithin30Days => "Licence expiry alert: within 30 days", 
		CustomerLicenceExpiryAlert.ExpiringWithin60Days => "Licence expiry alert: within 60 days", 
		_ => string.Empty, 
	};

	public string StatusText
	{
		get
		{
			if (!Customer.IsActive)
			{
				return "Blocked";
			}
			return "Active";
		}
	}
}
