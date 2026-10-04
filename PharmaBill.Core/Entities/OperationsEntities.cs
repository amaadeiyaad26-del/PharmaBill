namespace PharmaBill.Core.Entities;

public sealed class ScheduleRegisterEntry : EntityBase
{
    public string RegisterType { get; set; } = string.Empty;
    public Guid? SaleId { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? DrugId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal? Quantity { get; set; }
    public string? BatchNo { get; set; }
    public DateTime EntryAtUtc { get; set; } = DateTime.UtcNow;
    public string? PatientName { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerPhone { get; set; }
    public string? BuyerLicenceNumber { get; set; }
    public string? PrescriberName { get; set; }
    public string? PrescriberRegistrationNumber { get; set; }
    public string? Notes { get; set; }
}

public sealed class AuditLog : EntityBase
{
    public Guid? UserId { get; set; }
    public DateTime ActionAtUtc { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? Details { get; set; }
}

public sealed class ChangeLog : EntityBase
{
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public string Payload { get; set; } = string.Empty;
}

public sealed class SyncConflict : EntityBase
{
    public string EntityName { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string LocalPayload { get; set; } = string.Empty;
    public string RemotePayload { get; set; } = string.Empty;
    public string? Resolution { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

public sealed class DeviceInfo : EntityBase
{
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public DateTime? LastSyncAtUtc { get; set; }
    public bool IsCurrentDevice { get; set; }
}

public sealed class NumberSeries : EntityBase
{
    public string SeriesPrefix { get; set; } = string.Empty;
    public string FinancialYear { get; set; } = string.Empty;
    public long LastNumber { get; set; }
}
