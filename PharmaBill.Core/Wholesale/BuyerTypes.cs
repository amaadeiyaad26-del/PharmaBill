
namespace PharmaBill.Core.Wholesale;

/// <summary>
/// Buyer-type names. "Distributor" is the single wholesale-trade buyer type; wholesaler and stockist are
/// common names for the same buyer and are stored as Distributor so the customer list and licence rules
/// never split across synonyms.
/// </summary>
public static class BuyerTypes
{
	public const string Distributor = "Distributor";

	/// <summary>Shown in pickers so users searching for any of the common words find it; stored as <see cref="Distributor"/>.</summary>
	public const string DistributorLabel = "Distributor / Wholesaler";

	private static readonly HashSet<string> DistributorAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"Wholesaler",
		"Wholesale",
		"Stockist",
		"Distributor/Wholesaler",
		DistributorLabel
	};

	/// <summary>The stored form of a buyer type: aliases of Distributor collapse to it; anything else is trimmed.</summary>
	public static string? Canonical(string? buyerType)
	{
		if (string.IsNullOrWhiteSpace(buyerType))
		{
			return buyerType;
		}
		string text = string.Join(' ', buyerType.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		return DistributorAliases.Contains(text) || DistributorAliases.Contains(text.Replace(" / ", "/")) ? Distributor : text;
	}

	public static bool IsAlias(string? buyerType) =>
		!string.IsNullOrWhiteSpace(buyerType) && !string.Equals(Canonical(buyerType), buyerType, StringComparison.Ordinal);

	public static IEnumerable<string> AliasesOf(string canonical) =>
		string.Equals(canonical, Distributor, StringComparison.OrdinalIgnoreCase) ? DistributorAliases : Enumerable.Empty<string>();
}
