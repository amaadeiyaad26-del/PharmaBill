using System;
using System.Collections.Generic;
using System.Linq;

namespace PharmaBill.Data.Services;

public sealed record RetailPatientMatch(Guid PatientId, string Name, string Phone, string? Address, int LoyaltyPoints);

public sealed record RetailPatientBillLine(Guid DrugId, string DrugName, decimal Quantity);

public sealed record RetailPatientBill(Guid SaleId, string InvoiceNo, DateTime SaleAtUtc, decimal TotalAmount, decimal PaidAmount, string? PaymentStatus, IReadOnlyList<RetailPatientBillLine> Lines)
{
	public string MedicineSummary => string.Join(", ", Lines.Select((RetailPatientBillLine line) => line.DrugName));
}
