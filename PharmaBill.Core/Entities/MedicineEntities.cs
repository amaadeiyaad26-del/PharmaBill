namespace PharmaBill.Core.Entities;

public sealed class Drug : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? BrandName { get; set; }
    public string? Strength { get; set; }
    public string? DosageForm { get; set; }
    public string? Unit { get; set; }
    public string? Barcode { get; set; }
    public string? Schedule { get; set; }
    public string? HsnCode { get; set; }
    public decimal? GstRate { get; set; }
    public decimal? Mrp { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal ReorderLevel { get; set; }
    public Guid? CatalogMedicineId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class CatalogMedicine : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? BrandName { get; set; }
    public string? Strength { get; set; }
    public string? DosageForm { get; set; }
    public string? Manufacturer { get; set; }
    public string? SourceId { get; set; }
    public decimal? ReferencePrice { get; set; }
    public bool IsDiscontinued { get; set; }
    public string? Type { get; set; }
    public string? PackSizeLabel { get; set; }
    public string? ShortComposition1 { get; set; }
    public string? ShortComposition2 { get; set; }
    public string CompositionKey { get; set; } = string.Empty;
}

public sealed class CatalogInfo : EntityBase
{
    public string NameKey { get; set; } = string.Empty;
    public Guid? CatalogMedicineId { get; set; }
    public string? Schedule { get; set; }
    public bool IsHabitForming { get; set; }
    public bool RequiresPrescription { get; set; }
    public string? RegisterType { get; set; }
    public string? Notes { get; set; }
    public string? TherapeuticClass { get; set; }
    public string? ChemicalClass { get; set; }
    public string? ActionClass { get; set; }
    public string? Use { get; set; }
}

public sealed class CatalogImportState : EntityBase
{
    public string ImportKey { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileFingerprint { get; set; } = string.Empty;
    public long LastProcessedRecord { get; set; }
    public long ImportedRows { get; set; }
    public long SkippedRows { get; set; }
    public string Status { get; set; } = "NotStarted";
    public string? LastError { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}

public sealed class ScheduleOverride : EntityBase
{
    public Guid DrugId { get; set; }
    public string Schedule { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
}
