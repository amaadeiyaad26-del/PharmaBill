namespace PharmaBill.Core.Entities;

public sealed class Supplier : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Gstin { get; set; }
    public string? DrugLicenceNumber { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class Batch : EntityBase
{
    public Guid DrugId { get; set; }
    public Guid? SupplierId { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public DateOnly? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal? Ptr { get; set; }
    public decimal? Pts { get; set; }
    public decimal? SalePrice { get; set; }
    public decimal? Mrp { get; set; }
    public string? Rack { get; set; }
}

public enum PackLevel
{
    Unit = 0,
    Strip = 1,
    Box = 2,
    Carton = 3
}

public sealed class DrugPackLevel : EntityBase
{
    public Guid DrugId { get; set; }
    public PackLevel Level { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal UnitsPerPack { get; set; }
}

public sealed class CustomerCategoryPrice : EntityBase
{
    public Guid? DrugId { get; set; }
    public string PriceCategory { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class TradeDiscount : EntityBase
{
    public Guid? DrugId { get; set; }
    public string PriceCategory { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class WholesaleScheme : EntityBase
{
    public Guid? DrugId { get; set; }
    public string? PriceCategory { get; set; }
    public decimal BuyQuantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly EndsOn { get; set; }
}

public sealed class WholesaleRateHistory : EntityBase
{
    public Guid CustomerId { get; set; }
    public Guid DrugId { get; set; }
    public Guid BatchId { get; set; }
    public Guid InvoiceId { get; set; }
    public DateTime SoldAtUtc { get; set; } = DateTime.UtcNow;
    public decimal UnitRate { get; set; }
    public decimal Quantity { get; set; }
}

public sealed class StockMovement : EntityBase
{
    public Guid BatchId { get; set; }
    public Guid DrugId { get; set; }
    public decimal QuantityChange { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public Guid? ReferenceId { get; set; }
    public DateTime MovementAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

public sealed class PurchaseInvoice : EntityBase
{
    public Guid SupplierId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Status { get; set; }
    public string? Notes { get; set; }
}

public sealed class PurchaseItem : EntityBase
{
    public Guid PurchaseInvoiceId { get; set; }
    public Guid DrugId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public string BatchNo { get; set; } = string.Empty;
    public DateOnly? ExpiryDate { get; set; }
    public decimal? Mrp { get; set; }
    public decimal? Ptr { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class PurchaseReturn : EntityBase
{
    public Guid SupplierId { get; set; }
    public string ReturnNo { get; set; } = string.Empty;
    public DateOnly ReturnDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class PurchaseReturnItem : EntityBase
{
    public Guid PurchaseReturnId { get; set; }
    public Guid BatchId { get; set; }
    public Guid DrugId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class StockAdjustment : EntityBase
{
    public Guid BatchId { get; set; }
    public Guid DrugId { get; set; }
    public decimal QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime AdjustedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class StockVerificationSession : EntityBase
{
    public string SessionNo { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PostedAtUtc { get; set; }
    public string Status { get; set; } = "Open";
}

public sealed class StockVerificationItem : EntityBase
{
    public Guid SessionId { get; set; }
    public Guid BatchId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal AdjustmentQuantity { get; set; }
}

public sealed class ExpiryWriteOff : EntityBase
{
    public Guid BatchId { get; set; }
    public Guid DrugId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime WrittenOffAtUtc { get; set; } = DateTime.UtcNow;
}
