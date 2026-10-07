using System;

namespace PharmaBill.Data.Services;

public sealed record AddStockInput(Guid? DrugId, Guid? CatalogMedicineId, string MedicineName, string? Composition, string? Manufacturer, string Schedule, string BatchNo, DateOnly ExpiryDate, decimal Mrp, decimal PurchaseRate, decimal Quantity, decimal FreeQuantity, decimal PackSize, decimal GstRate, Guid SupplierId, string SupplierInvoiceNo, DateOnly InvoiceDate, string? Rack);
