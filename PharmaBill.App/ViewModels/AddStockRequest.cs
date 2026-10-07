using System;

namespace PharmaBill.App.ViewModels;

public sealed record AddStockRequest(Guid? DrugId, Guid? CatalogMedicineId, string MedicineName, string? Composition, string? Manufacturer);
