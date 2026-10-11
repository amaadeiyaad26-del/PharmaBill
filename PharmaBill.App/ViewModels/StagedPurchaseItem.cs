namespace PharmaBill.App.ViewModels;

/// <summary>A provisional row read from a supplier invoice; shown for review and only copied to the purchase table when the chemist applies it.</summary>
public sealed record StagedPurchaseItem(string ItemName, bool MatchedInMaster, string Batch, string Expiry, decimal Quantity, decimal Free, decimal Mrp, decimal Rate, decimal Gst)
{
	public string MatchText => MatchedInMaster ? "Matched" : "New / unmatched";
}
