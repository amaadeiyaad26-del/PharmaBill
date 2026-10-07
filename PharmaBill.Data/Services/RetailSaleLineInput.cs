using System;

namespace PharmaBill.Data.Services;

public sealed record RetailSaleLineInput(Guid DrugId, Guid BatchId, decimal Quantity, decimal DiscountAmount, decimal UnitPrice);
