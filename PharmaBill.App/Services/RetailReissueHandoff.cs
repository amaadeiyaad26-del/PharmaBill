
namespace PharmaBill.App.Services;

/// <summary>One medicine from a retail bill that was cancelled for re-issue. Batches are picked FEFO when re-billed.</summary>
public sealed record RetailReissueLine(Guid DrugId, string DrugName, decimal Quantity);

/// <summary>The customer and items of a retail bill that was cancelled with a full credit note so it can be re-billed.</summary>
public sealed record RetailReissueRequest(string OriginalInvoiceNo, string PatientName, string? PatientPhone, IReadOnlyList<RetailReissueLine> Lines);

/// <summary>Hands a cancelled retail bill from Recent Bills to the Retail Billing page, which loads it as a new draft.</summary>
public sealed class RetailReissueHandoff
{
	public RetailReissueRequest? Pending { get; set; }
}
