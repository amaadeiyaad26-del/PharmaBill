using System;

namespace PharmaBill.Data.Services;

public sealed record CatalogueBarcodeMatch(Guid DrugId, string Name, string? BrandName, string? Strength, string? Barcode, string? BatchNo);
