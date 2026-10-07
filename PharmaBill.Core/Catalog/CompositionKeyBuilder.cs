using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace PharmaBill.Core.Catalog;

public static partial class CompositionKeyBuilder
{
	public static string Build(params string?[] compositions)
	{
		return string.Join("|", (from value in compositions
			where !string.IsNullOrWhiteSpace(value) && !value.Trim().Equals("NA", StringComparison.OrdinalIgnoreCase)
			select Normalize(value) into value
			where value.Length > 0
			select value).Distinct(StringComparer.Ordinal).OrderBy((string value) => value, StringComparer.Ordinal));
	}

	private static string Normalize(string value)
	{
		string input = WhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");
		input = PunctuationRegex().Replace(input, " ");
		return WhitespaceRegex().Replace(input, " ").Trim();
	}

	[GeneratedRegex("[^\\p{L}\\p{N}.%]+", RegexOptions.CultureInvariant)]
	private static partial Regex PunctuationRegex();

	[GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
	private static partial Regex WhitespaceRegex();
}
