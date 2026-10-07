using System;

namespace PharmaBill.Data.Services;

public sealed record MedicineSearchResult(Guid CatalogMedicineId, string Name, string? Composition, string CompositionKey, string? Manufacturer, string? PackSize, decimal? ReferencePrice, bool IsHabitForming, decimal StockQuantity, bool IsInStock, DateOnly? NearestExpiry = null, string? Rack = null, decimal? Mrp = null, Guid? DrugId = null, string? OtherBranchAvailability = null);
