using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

/// <summary>A quotation opened for conversion into a retail or wholesale bill.</summary>
public sealed record QuotationConversionRequest(Guid QuotationId, string QuoteNo, string Mode, Guid? CustomerId, string PartyName, string? PartyPhone, IReadOnlyList<QuotationLineInput> Lines);

/// <summary>
/// Hands a quotation from the Quotations page to the matching billing page, which loads its lines as a draft.
/// The quotation is marked converted only after that bill is posted.
/// </summary>
public sealed class QuotationConversionHandoff
{
	public QuotationConversionRequest? Pending { get; set; }
}
