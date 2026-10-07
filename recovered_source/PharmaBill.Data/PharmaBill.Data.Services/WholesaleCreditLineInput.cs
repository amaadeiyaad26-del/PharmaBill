using System;

namespace PharmaBill.Data.Services;

public sealed record WholesaleCreditLineInput(Guid InvoiceItemId, decimal Quantity, bool Restock);
