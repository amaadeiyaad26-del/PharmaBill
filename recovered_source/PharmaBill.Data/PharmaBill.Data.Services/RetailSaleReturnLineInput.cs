using System;

namespace PharmaBill.Data.Services;

public sealed record RetailSaleReturnLineInput(Guid SaleItemId, decimal Quantity);
