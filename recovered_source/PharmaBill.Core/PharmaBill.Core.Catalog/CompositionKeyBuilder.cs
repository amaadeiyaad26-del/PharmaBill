using System;
using System.CodeDom.Compiler;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;

namespace PharmaBill.Core.Catalog;

public static class CompositionKeyBuilder
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
	[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
	private static Regex PunctuationRegex()
	{
		return _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__PunctuationRegex_14.Instance;
	}

	[GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
	[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
	private static Regex WhitespaceRegex()
	{
		return _003CRegexGenerator_g_003EFC6F1FA4B5FDA465300078EEE80D495351AD5434EFF4427A9337003D827FAF902__WhitespaceRegex_15.Instance;
	}
}
