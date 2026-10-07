using System;

namespace PharmaBill.Core.Wholesale;

public static class CustomerLicenceExpiryAlerts
{
	public static CustomerLicenceExpiryAlert Evaluate(DateOnly? expiresOn, DateOnly today)
	{
		if (!expiresOn.HasValue)
		{
			return CustomerLicenceExpiryAlert.None;
		}
		int num = expiresOn.Value.DayNumber - today.DayNumber;
		if (num <= 30)
		{
			if (num < 0)
			{
				return CustomerLicenceExpiryAlert.Expired;
			}
			return CustomerLicenceExpiryAlert.ExpiringWithin30Days;
		}
		if (num <= 60)
		{
			return CustomerLicenceExpiryAlert.ExpiringWithin60Days;
		}
		return CustomerLicenceExpiryAlert.None;
	}
}
