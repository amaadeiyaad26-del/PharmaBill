using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record InvoiceDetail(string DocumentType, string InvoiceNo, DateTime AtLocal, string Party, string Phone, string Address, string PrescriberName, string PrescriberRegistrationNumber, string PrescriptionReference, string PaymentStatus, decimal Subtotal, decimal TaxAmount, decimal DiscountAmount, decimal TotalAmount, decimal PaidAmount, string Notes, IReadOnlyList<InvoiceDetailLine> Lines);
