using System;

namespace PharmaBill.Data.Services;

public sealed record RecentRetailBill(Guid SaleId, string InvoiceNo, DateTime SaleAtUtc, string PatientName, string? PatientPhone, decimal TotalAmount, decimal PaidAmount, bool IsLocked = true, Guid? BilledByUserId = null);
