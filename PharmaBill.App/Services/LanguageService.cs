using System.Windows;

namespace PharmaBill.App.Services;

public sealed class LanguageService : ILanguageService
{
    private static readonly IReadOnlyDictionary<string, string> LanguageDictionaries =
        new Dictionary<string, string>
        {
            ["English"] = "Resources/Strings.en.xaml",
            ["Hindi"] = "Resources/Strings.hi.xaml",
            ["Urdu"] = "Resources/Strings.ur.xaml"
        };

    public IReadOnlyList<string> LanguageOptions { get; } = [.. LanguageDictionaries.Keys];

    public FlowDirection FlowDirection { get; private set; } = FlowDirection.LeftToRight;

    public event EventHandler? LanguageChanged;

    public void ApplyLanguage(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        if (!LanguageDictionaries.TryGetValue(language, out var dictionaryPath))
        {
            throw new ArgumentOutOfRangeException(nameof(language), language, "The selected language is not supported.");
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var currentLanguage = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (currentLanguage is null)
        {
            throw new InvalidOperationException("The application language resource dictionary is missing.");
        }

        var replacement = new ResourceDictionary { Source = new Uri(dictionaryPath, UriKind.Relative) };
        dictionaries[dictionaries.IndexOf(currentLanguage)] = replacement;
        FlowDirection = language == "Urdu" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string GetString(string resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        if (Application.Current.TryFindResource(resourceKey) is string value)
        {
            return value;
        }

        throw new KeyNotFoundException($"The localization resource '{resourceKey}' was not found.");
    }
}
