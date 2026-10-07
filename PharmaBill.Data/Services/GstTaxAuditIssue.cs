using System;

namespace PharmaBill.Data.Services;

public sealed record GstTaxAuditIssue(GstTaxAuditSeverity Severity, string InvoiceNo, DateOnly InvoiceDate, string Category, string Message);
