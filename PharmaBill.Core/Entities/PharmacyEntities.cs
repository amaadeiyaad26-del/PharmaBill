namespace PharmaBill.Core.Entities;

public enum BusinessMode
{
    Retail = 0,
    Wholesaler = 1,
    Both = 2
}

public sealed class PharmacyProfile : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Address { get; set; }
    public string? State { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Gstin { get; set; }
    public string? Pan { get; set; }
    public BusinessMode BusinessMode { get; set; } = BusinessMode.Retail;
    public string? CompetentPersonName { get; set; }
    public string? CompetentPersonQualification { get; set; }
    public string? CompetentPersonRegistrationNumber { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfsc { get; set; }
    public string? UpiId { get; set; }
    public string InvoicePrefix { get; set; } = "WIN1";
    public string WholesaleLicenceTypesJson { get; set; } = "[]";
    public string WholesaleBuyerLicenceRulesJson { get; set; } = "{}";
    public DateTime? TrialStartedAtUtc { get; set; }
    public DateTime? LastEntitlementCheckAtUtc { get; set; }
    public bool ClockRollbackDetected { get; set; }
    public string? CurrencyCode { get; set; }
    public string? TimeZoneId { get; set; }
}

public sealed class LicenceRecord : EntityBase
{
    public string LicenceNumber { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public string? IssuingAuthority { get; set; }
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? Notes { get; set; }
    public string? DocumentPath { get; set; }
}

public enum UserRole
{
    Owner = 0,
    Manager = 1,
    Pharmacist = 2,
    BillingClerk = 3,
    Salesman = 4,
    Accountant = 5
}

public sealed class AppUser : EntityBase
{
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public UserRole Role { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public int IdleLockMinutes { get; set; } = 10;
    public bool UseWindowsHello { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
}
