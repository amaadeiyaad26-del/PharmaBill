using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record SaveWholesaleInvoiceInput(Guid CustomerId, IReadOnlyList<WholesaleInvoiceLineInput> Items, decimal PaidAmount, string PaymentMethod, string? PaymentReference, bool ConfirmNearExpiry, int NearExpiryWarningDays, bool OverrideCreditOrOverdue, string? OverrideReason, string? TransportDetails, string? VehicleNumber, string? EWayBillNumber, string? Irn, string? Notes, DateTime? InvoiceAtUtc = null);
