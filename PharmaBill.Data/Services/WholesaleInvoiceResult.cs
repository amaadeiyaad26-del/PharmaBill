using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Services;

public sealed record WholesaleInvoiceResult(WholesaleInvoice Invoice, decimal CgstAmount, decimal SgstAmount, decimal IgstAmount, decimal RoundOff, decimal OutstandingAfterPosting, bool HasOverdueBalance);
