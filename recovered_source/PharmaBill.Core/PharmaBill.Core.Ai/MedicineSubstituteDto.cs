using System;

namespace PharmaBill.Core.Ai;

public sealed record MedicineSubstituteDto(Guid CatalogMedicineId, Guid? DrugId, string Name, string? Composition, string CompositionKey, string? Manufacturer, string? PackSize, bool IsHabitForming, SubstituteMatchType MatchType, decimal AvailableQuantity, bool CoversTargetQuantity, DateOnly? FefoExpiry, decimal? CustomerPrice, decimal? MarginPercent, double Score, string Reason);
