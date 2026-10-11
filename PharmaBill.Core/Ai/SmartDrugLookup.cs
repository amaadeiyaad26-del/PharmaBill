using System.Globalization;
using System.Text.Json;

namespace PharmaBill.Core.Ai;

/// <summary>
/// Parses AI/web JSON replies into <see cref="SmartDrugSuggestion"/>.
/// Canonical suggestion/outcome types live in SmartDrugSuggestion.cs;
/// the lookup service contract lives in ISmartDrugLookupService.cs.
/// </summary>
public static class SmartDrugSuggestionParser
{
	public const decimal MaxReasonableMrp = 1_000_000m;

	/// <summary>Parses the model reply (plain JSON, or JSON inside a code fence / prose). Returns null if unusable.</summary>
	public static SmartDrugSuggestion? Parse(string? text)
	{
		string? json = ExtractJsonObject(text);
		if (json == null)
		{
			return null;
		}

		try
		{
			using JsonDocument doc = JsonDocument.Parse(json);
			if (doc.RootElement.ValueKind != JsonValueKind.Object)
			{
				return null;
			}

			JsonElement root = doc.RootElement;
			string? brand = GetString(root, "brandName", "brand_name", "brand", "name");
			string? generic = GetString(root, "genericName", "composition", "generic", "salt");
			string? manufacturer = GetString(root, "manufacturer", "company");
			string? pack = GetString(root, "packSizeLabel", "packLabel", "packaging", "unit", "pack");
			string? hsn = GetString(root, "hsnCode", "hsn");
			if (hsn != null && (hsn.Length < 4 || hsn.Length > 8 || !hsn.All(char.IsDigit)))
			{
				hsn = null;
			}

			decimal? gst = GetDecimal(root, "gstRate", "gst", "gstPercent");
			if (gst is < 0m or > 100m)
			{
				gst = null;
			}

			decimal? mrp = GetDecimal(root, "approximateMrp", "approxMrp", "mrp");
			if (mrp is <= 0m || mrp > MaxReasonableMrp)
			{
				mrp = null;
			}

			if (brand == null && generic == null && manufacturer == null)
			{
				return null;
			}

			return new SmartDrugSuggestion(
				BrandName: string.IsNullOrWhiteSpace(brand) ? (generic ?? "Unknown") : brand,
				GenericName: generic,
				Manufacturer: manufacturer,
				PackSizeLabel: pack,
				GstRate: gst ?? 12m,
				HsnCode: hsn ?? "3004",
				ApproximateMrp: mrp is null ? null : Math.Round(mrp.Value, 2, MidpointRounding.AwayFromZero));
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string? ExtractJsonObject(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		int start = text.IndexOf('{');
		int end = text.LastIndexOf('}');
		return start >= 0 && end > start ? text.Substring(start, end - start + 1) : null;
	}

	private static string? GetString(JsonElement root, params string[] names)
	{
		foreach (string name in names)
		{
			foreach (JsonProperty p in root.EnumerateObject())
			{
				if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
				{
					string? value = p.Value.GetString()?.Trim();
					if (!string.IsNullOrEmpty(value) && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase) && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
					{
						return value.Length > 200 ? value[..200] : value;
					}
				}
			}
		}

		return null;
	}

	private static decimal? GetDecimal(JsonElement root, params string[] names)
	{
		foreach (string name in names)
		{
			foreach (JsonProperty p in root.EnumerateObject())
			{
				if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out decimal d))
				{
					return d;
				}

				if (p.Value.ValueKind == JsonValueKind.String && decimal.TryParse(p.Value.GetString()?.Replace("₹", "").Replace("%", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal s))
				{
					return s;
				}
			}
		}

		return null;
	}
}
