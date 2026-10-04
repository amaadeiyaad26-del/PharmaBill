namespace PharmaBill.Core.Entities;

public sealed class Patient : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Sex { get; set; }
}

public sealed class Prescription : EntityBase
{
    public Guid? PatientId { get; set; }
    public string? PrescriberName { get; set; }
    public string? PrescriberRegistrationNumber { get; set; }
    public DateOnly? PrescriptionDate { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public sealed class Sale : EntityBase
{
    public Guid? PatientId { get; set; }
    public Guid? PrescriptionId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateTime SaleAtUtc { get; set; } = DateTime.UtcNow;
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string? PaymentStatus { get; set; }
    public string? Notes { get; set; }
}

public sealed class SaleItem : EntityBase
{
    public Guid SaleId { get; set; }
    public Guid DrugId { get; set; }
    public Guid BatchId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Customer : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Gstin { get; set; }
    public string? BuyerType { get; set; }
    public bool IsActive { get; set; } = true;
    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; }
    public string? PriceCategory { get; set; }
    public string? Route { get; set; }
    public string? Salesman { get; set; }
    public decimal OpeningBalance { get; set; }
    public string? State { get; set; }
    public string? StateDrugControlPortalUrl { get; set; }
    public string? VerifiedBy { get; set; }
    public DateTime? VerifiedOnUtc { get; set; }
    public string? VerificationMethod { get; set; }
}

public sealed class CustomerLicence : EntityBase
{
    public Guid CustomerId { get; set; }
    public string LicenceNumber { get; set; } = string.Empty;
    public string LicenceType { get; set; } = string.Empty;
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? IssuingAuthority { get; set; }
    public string? DocumentPath { get; set; }
    public string? Authorisation { get; set; }
    public string? Notes { get; set; }
}

public sealed class WholesaleInvoice : EntityBase
{
    public Guid CustomerId { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public DateTime InvoiceAtUtc { get; set; } = DateTime.UtcNow;
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public string? PaymentStatus { get; set; }
    public string? TransportDetails { get; set; }
    public string? VehicleNumber { get; set; }
    public string? EWayBillNumber { get; set; }
    public string? Irn { get; set; }
    public string Status { get; set; } = "Posted";
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public string? Notes { get; set; }
}

public sealed class WholesaleInvoiceItem : EntityBase
{
    public Guid WholesaleInvoiceId { get; set; }
    public Guid DrugId { get; set; }
    public Guid BatchId { get; set; }
    public decimal Quantity { get; set; }
    public decimal FreeQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal CgstAmount { get; set; }
    public decimal SgstAmount { get; set; }
    public decimal IgstAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Receipt : EntityBase
{
    public Guid? CustomerId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? WholesaleInvoiceId { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public DateTime ReceiptAtUtc { get; set; } = DateTime.UtcNow;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string? ChequeNumber { get; set; }
    public DateOnly? ChequeDate { get; set; }
    public string? BankName { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
}

public sealed class CustomerLedgerEntry : EntityBase
{
    public Guid CustomerId { get; set; }
    public DateTime EntryAtUtc { get; set; } = DateTime.UtcNow;
    public string EntryType { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNo { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Notes { get; set; }
}

public sealed class SupplierLedgerEntry : EntityBase
{
    public Guid SupplierId { get; set; }
    public DateTime EntryAtUtc { get; set; } = DateTime.UtcNow;
    public string EntryType { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNo { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Notes { get; set; }
}

public sealed class ReturnNote : EntityBase
{
    public string ReturnNo { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public DateTime ReturnAtUtc { get; set; } = DateTime.UtcNow;
    public decimal TotalAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public sealed class SaleReturnItem : EntityBase
{
    public Guid ReturnNoteId { get; set; }
    public Guid SaleItemId { get; set; }
    public Guid BatchId { get; set; }
    public Guid DrugId { get; set; }
    public decimal Quantity { get; set; }
    public decimal CreditAmount { get; set; }
    public bool Restocked { get; set; }
}

public sealed class WholesaleReturnItem : EntityBase
{
    public Guid ReturnNoteId { get; set; }
    public Guid WholesaleInvoiceItemId { get; set; }
    public Guid BatchId { get; set; }
    public Guid DrugId { get; set; }
    public decimal Quantity { get; set; }
    public decimal CreditAmount { get; set; }
    public bool Restocked { get; set; }
    public bool Quarantined { get; set; }
}
