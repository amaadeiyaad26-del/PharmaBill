using System;

namespace PharmaBill.Data.Services;

public sealed record DrugRecordsDrugChoice(Guid DrugId, string Name, string? Composition, string? BrandName);
