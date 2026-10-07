using System;

namespace PharmaBill.Data.Services;

public sealed record RetailBillPrintLine(string DrugName, string BatchNo, DateOnly? ExpiryDate, decimal Quantity, decimal UnitPrice, decimal TaxRate, decimal TaxAmount, decimal DiscountAmount, decimal LineTotal, string? HsnCode = null);
