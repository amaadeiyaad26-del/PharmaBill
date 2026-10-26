using PharmaBill.Core.Gst;

namespace PharmaBill.Data.Services;

/// <param name="Tax">HSN and GST rate entered at the counter for a product that has none recorded. Ignored when the product has its own.</param>
public sealed record RetailSaleLineInput(Guid DrugId, Guid BatchId, decimal Quantity, decimal DiscountAmount, decimal UnitPrice, ProductTaxDetails? Tax = null);
