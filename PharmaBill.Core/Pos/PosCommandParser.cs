using System.Globalization;
using System.Text.RegularExpressions;

namespace PharmaBill.Core.Pos;

/// <summary>How the quantity in a command was counted.</summary>
public enum PosQuantityUnit
{
	/// <summary>Whole packs as stocked: strips, bottles, tubes, boxes. No unit word also means packs.</summary>
	Pack,

	/// <summary>Single tablets or capsules, converted to packs when the pack size is known.</summary>
	Piece
}

/// <summary>
/// "sold 1 strip of pacimol to mr x": what to sell, how much, and to whom. The medicine is still text here; it is
/// matched against stock and the drug bank separately.
/// </summary>
public sealed record PosSaleCommand(decimal Quantity, PosQuantityUnit Unit, string UnitWord, string MedicineText, string? Customer);

/// <summary>
/// Reads a short sale written the way a pharmacist would say it. Local and rule-based: no network, no AI. Understands an
/// optional verb (sold, sell, give, bill…), a quantity in digits or words, a pack or piece unit, the medicine, and
/// "to"/"for" a customer, in either order of quantity and medicine: "1 strip paracetamol to John", "dolo 650 x2 for Mrs Rao".
/// </summary>
public static partial class PosCommandParser
{
	private static readonly HashSet<string> Verbs = new(StringComparer.Ordinal)
	{
		"sold", "sell", "sale", "selling", "bill", "billed", "give", "gave", "given", "add", "issue", "issued", "dispense", "dispensed"
	};

	private static readonly Dictionary<string, decimal> NumberWords = new(StringComparer.Ordinal)
	{
		["a"] = 1, ["an"] = 1, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
		["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["half"] = 0.5m
	};

	private static readonly Dictionary<string, (PosQuantityUnit Unit, string Word)> Units = BuildUnits();

	private static readonly string[] Honorifics = ["mr", "mrs", "ms", "miss", "dr", "shri", "smt", "sri", "master", "baby"];

	[GeneratedRegex(@"^x?(\d+(?:\.\d+)?)x?$")]
	private static partial Regex Number();

	[GeneratedRegex(@"\s+(?:to|for)\s+", RegexOptions.IgnoreCase)]
	private static partial Regex CustomerSeparator();

	public static bool TryParse(string? text, out PosSaleCommand? command, out string error)
	{
		command = null;
		string input = (text ?? string.Empty).Trim().TrimEnd('.', '!', ',');
		if (input.Length == 0)
		{
			error = "Type a sale, for example: 1 strip paracetamol to John.";
			return false;
		}

		// "… to Mr X" / "… for Mrs Rao": the last one, so a medicine name containing "to" is left alone.
		string? customer = null;
		MatchCollection separators = CustomerSeparator().Matches(input);
		if (separators.Count > 0)
		{
			Match last = separators[^1];
			string after = input[(last.Index + last.Length)..].Trim();
			if (after.Length > 0)
			{
				customer = CustomerName(after);
				input = input[..last.Index].Trim();
			}
		}

		List<string> words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
		if (words.Count > 0 && Verbs.Contains(words[0].ToLowerInvariant()))
		{
			words.RemoveAt(0);
		}

		decimal? quantity = null;
		PosQuantityUnit unit = PosQuantityUnit.Pack;
		string unitWord = "pack";

		// Quantity first: "1 strip of pacimol", "two pacimol", "a bottle of benadryl".
		if (words.Count > 1 && TryQuantity(words[0], out decimal leading))
		{
			quantity = leading;
			words.RemoveAt(0);
			if (words.Count > 0 && Units.TryGetValue(words[0].ToLowerInvariant(), out var leadingUnit))
			{
				(unit, unitWord) = leadingUnit;
				words.RemoveAt(0);
			}
			if (words.Count > 1 && string.Equals(words[0], "of", StringComparison.OrdinalIgnoreCase))
			{
				words.RemoveAt(0);
			}
		}
		else
		{
			// Quantity last: "pacimol 2 strips", "dolo 650 x2". A bare number after the name is part of it (Dolo 650)
			// unless it is marked as a count with x or followed by a unit.
			if (words.Count > 2 && Units.TryGetValue(words[^1].ToLowerInvariant(), out var trailingUnit) && TryQuantity(words[^2], out decimal trailing))
			{
				quantity = trailing;
				(unit, unitWord) = trailingUnit;
				words.RemoveRange(words.Count - 2, 2);
			}
			else if (words.Count > 1 && words[^1].StartsWith('x') && TryQuantity(words[^1], out decimal times))
			{
				quantity = times;
				words.RemoveAt(words.Count - 1);
			}
		}

		string medicine = string.Join(' ', words).Trim();
		if (medicine.Length < 2)
		{
			error = "Which medicine? For example: 1 strip paracetamol to John.";
			return false;
		}
		if (quantity is <= 0m)
		{
			error = "The quantity must be more than 0.";
			return false;
		}

		command = new PosSaleCommand(quantity ?? 1m, unit, unitWord, medicine, customer);
		error = string.Empty;
		return true;
	}

	/// <summary>Tablets or capsules in one pack, from labels such as "strip of 10 tablets" or "15 Tablets / Strip".</summary>
	public static int? PiecesPerPack(string? packLabel)
	{
		if (string.IsNullOrWhiteSpace(packLabel))
		{
			return null;
		}
		Match match = Regex.Match(packLabel, @"(\d+)\s*(?:tablets?|tabs?|capsules?|caps?)\b", RegexOptions.IgnoreCase);
		return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int pieces) && pieces > 0 ? pieces : null;
	}

	private static bool TryQuantity(string word, out decimal quantity)
	{
		string lower = word.ToLowerInvariant();
		if (NumberWords.TryGetValue(lower, out quantity))
		{
			return true;
		}
		Match match = Number().Match(lower);
		return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity);
	}

	// "mr x" → "Mr X", "mrs rao" → "Mrs Rao": each word capitalised, honorifics kept.
	private static string CustomerName(string text) =>
		string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
		{
			string lower = word.ToLowerInvariant().TrimEnd('.');
			string cased = char.ToUpperInvariant(lower[0]) + lower[1..];
			return Honorifics.Contains(lower) ? cased + (word.EndsWith('.') ? "." : string.Empty) : char.ToUpperInvariant(word[0]) + word[1..];
		}));

	private static Dictionary<string, (PosQuantityUnit, string)> BuildUnits()
	{
		var units = new Dictionary<string, (PosQuantityUnit, string)>(StringComparer.Ordinal);
		void Add(PosQuantityUnit unit, string word, params string[] forms)
		{
			foreach (string form in forms)
			{
				units[form] = (unit, word);
			}
		}
		Add(PosQuantityUnit.Pack, "strip", "strip", "strips", "stp");
		Add(PosQuantityUnit.Pack, "pack", "pack", "packs", "packet", "packets", "pkt");
		Add(PosQuantityUnit.Pack, "box", "box", "boxes");
		Add(PosQuantityUnit.Pack, "bottle", "bottle", "bottles", "btl", "bot");
		Add(PosQuantityUnit.Pack, "tube", "tube", "tubes");
		Add(PosQuantityUnit.Pack, "vial", "vial", "vials", "ampoule", "ampoules", "amp");
		Add(PosQuantityUnit.Pack, "sachet", "sachet", "sachets");
		Add(PosQuantityUnit.Pack, "inhaler", "inhaler", "inhalers");
		Add(PosQuantityUnit.Piece, "tablet", "tab", "tabs", "tablet", "tablets");
		Add(PosQuantityUnit.Piece, "capsule", "cap", "caps", "capsule", "capsules");
		return units;
	}
}
