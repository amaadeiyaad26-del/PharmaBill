using System.Text.Json;

namespace PharmaBill.Core.Wholesale;

/// <summary>
/// Which drug-licence types a wholesale buyer must hold, per buyer type. Wholesale invoicing is refused
/// for a buyer whose type has no rule, so sensible defaults ship with the app.
/// </summary>
public static class BuyerLicenceRules
{
	/// <summary>Drugs and Cosmetics Rules forms: 20/21 retail sale, 20B/21B wholesale.</summary>
	public static IReadOnlyList<string> StandardLicenceTypes { get; } = new[] { "20", "21", "20B", "21B" };

	public static IReadOnlyDictionary<string, string[]> Defaults { get; } = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["Retailer"] = new[] { "20", "21" },
		["Hospital"] = new[] { "20", "21" },
		["Institution"] = new[] { "20", "21" },
		[BuyerTypes.Distributor] = new[] { "20B", "21B" }
		// "Other" deliberately has no default: an unknown buyer type must not inherit the permissive 20B/21B rule.
	};

	public const string TypicalExample = "Retailer → 20, 21; Distributor (wholesaler / stockist) → 20B, 21B";

	public const string WhereToSet = "Customers → Wholesale Buyers → \"Licence type rules for buyer types\"";

	public static string DefaultsJson => JsonSerializer.Serialize(Defaults);

	public static Dictionary<string, string[]> Parse(string? json)
	{
		Dictionary<string, string[]>? parsed = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, string[]>>(json);
		return new Dictionary<string, string[]>(parsed ?? new Dictionary<string, string[]>(), StringComparer.OrdinalIgnoreCase);
	}

	public static string[] AllowedFor(IReadOnlyDictionary<string, string[]> rules, string? buyerType)
	{
		if (string.IsNullOrWhiteSpace(buyerType))
		{
			return Array.Empty<string>();
		}
		string canonical = BuyerTypes.Canonical(buyerType)!;
		return rules.FirstOrDefault(rule => string.Equals(BuyerTypes.Canonical(rule.Key), canonical, StringComparison.OrdinalIgnoreCase) && rule.Value.Any(type => !string.IsNullOrWhiteSpace(type))).Value
			?.Where(type => !string.IsNullOrWhiteSpace(type)).ToArray() ?? Array.Empty<string>();
	}

	/// <summary>
	/// Folds rules stored under an alias (e.g. "Wholesaler") into the canonical buyer type. An alias rule moves
	/// across only when the canonical type has none; otherwise the canonical rule wins and the alias rule is dropped.
	/// Returns a description of each change for the audit trail.
	/// </summary>
	public static (string Json, IReadOnlyList<string> Changes) MergeAliasRules(string? json)
	{
		Dictionary<string, string[]> rules = Parse(json);
		List<string> changes = new List<string>();
		foreach (string key in rules.Keys.Where(BuyerTypes.IsAlias).ToList())
		{
			string canonical = BuyerTypes.Canonical(key)!;
			string[] aliasTypes = rules[key];
			rules.Remove(key);
			string[] existing = rules.TryGetValue(canonical, out string[]? value) ? value : Array.Empty<string>();
			if (existing.Length == 0 && aliasTypes.Length > 0)
			{
				rules[canonical] = aliasTypes;
				changes.Add($"Licence rule for buyer type '{key}' ({string.Join(", ", aliasTypes)}) moved to '{canonical}'.");
			}
			else
			{
				changes.Add($"Licence rule for buyer type '{key}' ({string.Join(", ", aliasTypes)}) removed; '{canonical}' keeps its rule ({string.Join(", ", existing)}).");
			}
		}
		return (JsonSerializer.Serialize(rules), changes);
	}

	/// <summary>Adds a default rule for every default buyer type that has none; never changes a configured rule.</summary>
	public static (string Json, bool Changed) AddMissingDefaults(string? json)
	{
		Dictionary<string, string[]> rules = Parse(json);
		bool changed = false;
		foreach ((string buyerType, string[] types) in Defaults)
		{
			if (AllowedFor(rules, buyerType).Length == 0)
			{
				rules[buyerType] = types;
				changed = true;
			}
		}
		return (JsonSerializer.Serialize(rules), changed);
	}

	/// <summary>Plain explanation shown when wholesale sales are blocked for lack of a rule; null when a rule exists.</summary>
	public static string? MissingRuleMessage(IReadOnlyDictionary<string, string[]> rules, string? buyerType)
	{
		if (rules.Values.All(types => types.Length == 0))
		{
			return "Wholesale billing is blocked: no licence-type rules are set for any buyer type. Set them in " + WhereToSet + " by ticking the licence types each buyer type must hold (typical: " + TypicalExample + ").";
		}
		if (buyerType != null && AllowedFor(rules, buyerType).Length == 0)
		{
			return "Sales to \"" + buyerType.Trim() + "\" buyers are blocked: no licence types are allowed for this buyer type. Set them in " + WhereToSet + " (typical: " + TypicalExample + ").";
		}
		return null;
	}
}
