using System.Text.RegularExpressions;

namespace PharmaBill.Core.Catalog;

public static partial class CompositionKeyBuilder
{
    public static string Build(params string?[] compositions)
    {
        return string.Join("|", compositions
            .Where(value => !string.IsNullOrWhiteSpace(value) &&
                            !value.Trim().Equals("NA", StringComparison.OrdinalIgnoreCase))
            .Select(value => Normalize(value!))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string Normalize(string value)
    {
        var normalized = WhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");
        normalized = PunctuationRegex().Replace(normalized, " ");
        return WhitespaceRegex().Replace(normalized, " ").Trim();
    }

    [GeneratedRegex(@"[^\p{L}\p{N}.%]+", RegexOptions.CultureInvariant)]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
