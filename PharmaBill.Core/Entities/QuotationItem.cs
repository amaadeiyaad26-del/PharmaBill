
namespace PharmaBill.Core.Entities;

public sealed class QuotationItem : EntityBase
{
	public Guid QuotationId { get; set; }

	public Guid DrugId { get; set; }

	/// <summary>Batch chosen on a wholesale quote. Null for retail, where batches are picked FEFO at billing.</summary>
	public Guid? BatchId { get; set; }

	public string DrugName { get; set; } = string.Empty;

	public decimal Quantity { get; set; }

	public decimal FreeQuantity { get; set; }

	public decimal UnitPrice { get; set; }

	public decimal DiscountAmount { get; set; }

	public decimal LineTotal { get; set; }
}
