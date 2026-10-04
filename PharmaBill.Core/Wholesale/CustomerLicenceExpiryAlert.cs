namespace PharmaBill.Core.Wholesale;

public enum CustomerLicenceExpiryAlert
{
    None,
    ExpiringWithin60Days,
    ExpiringWithin30Days,
    Expired
}

public static class CustomerLicenceExpiryAlerts
{
    public static CustomerLicenceExpiryAlert Evaluate(DateOnly? expiresOn, DateOnly today)
    {
        if (!expiresOn.HasValue)
        {
            return CustomerLicenceExpiryAlert.None;
        }

        var daysRemaining = expiresOn.Value.DayNumber - today.DayNumber;
        return daysRemaining switch
        {
            < 0 => CustomerLicenceExpiryAlert.Expired,
            <= 30 => CustomerLicenceExpiryAlert.ExpiringWithin30Days,
            <= 60 => CustomerLicenceExpiryAlert.ExpiringWithin60Days,
            _ => CustomerLicenceExpiryAlert.None
        };
    }
}
