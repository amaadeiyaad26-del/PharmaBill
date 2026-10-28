using System.Text.RegularExpressions;

namespace PharmaBill.Core.Gst;

/// <summary>An HSN code worked out from what the product is, and why that code was chosen.</summary>
public sealed record HsnSuggestion(string Code, string Reason)
{
	/// <summary>3004, from the dosage form alone: the code the catalogue pass may fill in by itself.</summary>
	public bool IsMedicament => Code == HsnClassifier.Medicament;
}

/// <summary>
/// Classifies a product's HSN code from its dosage form, pack and name. Almost everything a chemist sells is 3004
/// (medicaments in measured doses or retail packs); the exceptions are a short, stable list. This is a classification,
/// never a tax decision: nothing here returns or implies a GST rate.
/// </summary>
public static partial class HsnClassifier
{
	public const string Medicament = "3004";

	private const string MedicamentText = "medicaments in measured doses or retail packs";

	/// <summary>Vaccines and blood products: they look like any injection, so they are found by name.</summary>
	public static readonly IReadOnlyList<string> VaccineAndBloodProductNames =
	[
		"vaccine", "toxoid", "immunoglobulin", "antiserum", "anti serum", "anti-serum", "anti snake venom", "antisnake venom",
		"anti-snake venom", "antivenom", "antivenin", "anti venom", "albumin", "plasma", "factor viii", "factor ix", "factor vii",
		"prothrombin complex",
	];

	/// <summary>Hormonal contraceptives (3006 60): they look like any tablet or injection, so they are found by name.</summary>
	public static readonly IReadOnlyList<string> HormonalContraceptiveNames =
	[
		"contraceptive", "ovral", "mala-d", "mala d", "mala-n", "mala n", "i-pill", "ipill", "unwanted 72", "unwanted-72",
		"e-pill", "novelon", "loette", "yasmin", "femilon", "duoluton", "saheli", "chhaya", "centchroman", "ormeloxifene",
		"triquilar", "depo-provera", "depo provera",
	];

	private static readonly Regex VaccineName = WordList(VaccineAndBloodProductNames);
	private static readonly Regex AskUser = Words("husk", "isabgol", "ispaghula");
	private static readonly Regex ContraceptiveName = WordList(HormonalContraceptiveNames);

	// What the product is, word by word: exceptions to 3004 first, then the medicine dosage forms.
	private static readonly (Regex Pattern, string Code, string Reason)[] Exceptions =
	[
		(Words("condom", "condoms"), "4014", "Condoms → 4014, rubber goods (not a medicament)"),
		(Words("pregnancy test", "pregnancy kit", "prega news", "test kit", "test strip", "test strips", "test card", "rapid test", "ovulation test"), "3822", "Diagnostic test kit or strip → 3822, diagnostic reagents"),
		(Words("suture", "sutures", "catgut", "vicryl", "prolene", "ethilon", "mersilk"), "3006", "Sutures → 3006"),
		(Words("bandage", "bandages", "gauze", "dressing", "dressings", "crepe", "tulle", "cotton", "absorbent cotton", "cotton roll", "band aid", "band-aid", "surgical tape"), "3005", "Dressings and bandages → 3005"),
		(Words("syringe", "syringes", "needle", "needles", "cannula", "catheter", "lancet", "lancets", "scalp vein", "iv set", "infusion set", "bp monitor", "blood pressure monitor", "stethoscope"), "9018", "Syringes, needles and medical instruments → 9018 (Chapter 90)"),
		(Words("nebuliser", "nebulizer"), "9019", "Nebuliser → 9019 (Chapter 90)"),
		(Words("thermometer"), "9025", "Thermometer → 9025 (Chapter 90)"),
		(Words("glucometer", "glucose meter"), "9027", "Glucose meter → 9027 (Chapter 90)"),
		(Words("soap", "soaps", "soap bar"), "3401", "Medicated soap → 3401"),
		(Words("toothpaste", "tooth paste", "dentifrice", "mouthwash", "mouth wash"), "3306", "Toothpaste or mouthwash → 3306, oral hygiene"),
		(Words("deodorant", "talc", "talcum", "baby powder", "prickly heat powder"), "3307", "Deodorant or toilet powder → 3307"),
		(Words("face wash", "facewash", "cleanser", "moisturiser", "moisturizer", "sunscreen", "sun screen", "body lotion", "lip balm", "face serum", "serum", "cold cream", "fairness cream"), "3304", "Cosmetic or skin care → 3304"),
		(Words("protein powder", "protein supplement", "health drink", "nutrition drink", "nutritional supplement", "nutritional powder", "food supplement"), "2106", "Food supplement → 2106"),
	];

	// Shampoo is 3305 unless it carries a drug (ketoconazole and similar): then it is a medicament, 3004.
	private static readonly Regex Shampoo = Words("shampoo", "shampoos");
	private static readonly Regex ActiveIngredient = new(@"\d+(\.\d+)?\s*%|\b(ketoconazole|ciclopirox|selenium|zinc pyrithione|coal tar|permethrin|clobetasol|salicylic|climbazole|piroctone|minoxidil|povidone|lindane)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	// "Scrub" is an antiseptic (Betadine Surgical Scrub), a medicament.
	private static readonly Regex MedicineForm = Words(
		"tablet", "tablets", "tab", "tabs", "caplet", "caplets", "captab", "captabs", "tabcap", "tabcaps", "capsule", "capsules", "cap", "caps",
		"instacap", "instacaps", "redicap", "redicaps", "rheocap", "oxycaps", "nexcaps", "soflets", "softgel", "softgels",
		"syrup", "dry syrup", "suspension", "suspensions", "redimix", "rediuse", "elixir", "linctus", "expectorant", "mixture", "tonic", "emulsion",
		"injection", "injections", "inj", "infusion", "vial", "vials", "ampoule", "ampoules", "prefilled syringe", "penfill", "cartridge", "cartriges", "cartridges", "autopen", "flexpen",
		"ointment", "ointments", "cream", "creams", "gel", "gels", "jelly", "lotion", "lotions", "liniment", "paste", "foam", "nail lacquer", "paint", "mouth paint",
		"drop", "drops", "eye drop", "eye drops", "ear drop", "ear drops", "nasal drops", "solution", "solutions", "liquid", "oral liquid",
		"inhaler", "inhalers", "rotacap", "rotacaps", "respule", "respules", "respicap", "respicaps", "transcap", "transcaps", "transpule", "transpules",
		"transhaler", "multihaler", "autohaler", "diskettes", "spray", "sprays", "nasal spray", "mouth spray",
		"suppository", "suppositories", "pessary", "pessaries", "enema", "douche",
		"granules", "sachet", "sachets", "powder", "powders", "dusting powder", "lozenge", "lozenges", "pastilles", "chewing gum", "gargle",
		"patch", "patches", "transdermal", "plaster", "strip", "strips", "pellets", "particles", "kit", "combipack", "combikit", "scrub");

	/// <summary>
	/// The HSN code for a product, with the reason; null when nothing about it says what it is (then ask). The name is
	/// read only for what the product is (its form and the short lists above), never for its tax rate.
	/// </summary>
	public static HsnSuggestion? Classify(string? name, string? dosageForm = null, string? packLabel = null, string? composition = null)
	{
		string productName = (name ?? string.Empty).Trim();
		string form = FirstNonEmpty(dosageForm, PackForm(packLabel));

		// Not in the table yet: psyllium husk could be a medicament or a food product. Ask.
		if (AskUser.IsMatch(productName) || AskUser.IsMatch(form))
		{
			return null;
		}
		if (VaccineName.IsMatch(productName))
		{
			return new HsnSuggestion("3002", $"Vaccine or blood product (\"{VaccineName.Match(productName).Value}\") → 3002");
		}
		if (ContraceptiveName.IsMatch(productName))
		{
			return new HsnSuggestion("3006", $"Hormonal contraceptive (\"{ContraceptiveName.Match(productName).Value}\") → 3006");
		}

		// The pack or dosage form says what it is; failing that, the product's own name does.
		foreach (string text in new[] { form, productName })
		{
			if (text.Length == 0)
			{
				continue;
			}
			if (Shampoo.IsMatch(text))
			{
				string all = productName + " " + composition;
				return ActiveIngredient.IsMatch(all) || !string.IsNullOrWhiteSpace(composition)
					? new HsnSuggestion(Medicament, "Medicated shampoo (contains a drug) → 3004, " + MedicamentText)
					: new HsnSuggestion("3305", "Shampoo → 3305, hair preparations");
			}
			// A medicine form in the pack beats an instrument word ("prefilled syringe of 1 Injection" is an injection).
			if (ReferenceEquals(text, form) && MedicineForm.Match(text) is { Success: true } packMatch && !IsException(text))
			{
				return Medicine(packMatch.Value);
			}
			foreach ((Regex pattern, string code, string reason) in Exceptions)
			{
				if (pattern.IsMatch(text))
				{
					return new HsnSuggestion(code, reason);
				}
			}
			if (LastMatch(MedicineForm, text) is string found)
			{
				return Medicine(found);
			}
		}
		return null;
	}

	private static HsnSuggestion Medicine(string formWord) =>
		new(Medicament, $"{Capitalise(formWord)} → 3004, {MedicamentText}");

	private static bool IsException(string text) => Shampoo.IsMatch(text) || Exceptions.Any(exception => exception.Pattern.IsMatch(text));

	/// <summary>The form at the end of a pack label: "strip of 10 tablets" → "tablets", "bottle of 60 ml Syrup" → "Syrup".</summary>
	public static string PackForm(string? packLabel)
	{
		if (string.IsNullOrWhiteSpace(packLabel))
		{
			return string.Empty;
		}
		Match match = PackFormPattern().Match(packLabel.Trim());
		return match.Success ? match.Groups["form"].Value.Trim() : packLabel.Trim();
	}

	private static string? LastMatch(Regex pattern, string text)
	{
		MatchCollection matches = pattern.Matches(text);
		return matches.Count == 0 ? null : matches[^1].Value;
	}

	private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

	private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant();

	private static Regex Words(params string[] words) => WordList(words);

	private static Regex WordList(IEnumerable<string> words) =>
		new(@"(?<![\p{L}\p{N}])(" + string.Join("|", words.OrderByDescending(word => word.Length).Select(word => Regex.Escape(word).Replace(@"\ ", @"[\s-]*", StringComparison.Ordinal))) + @")(?![\p{L}\p{N}])",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

	[GeneratedRegex(@"\bof\s+(?:[\d.]+\s*(?:ml|gm|g|mg|kg|l|ltr|litre|mcg|iu|cm|m|units?|x)?\s*)?(?<form>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex PackFormPattern();
}
