using System.Windows;
using System.Windows.Media;

namespace PharmaBill.App.Services;

public sealed class ThemeService : IThemeService
{
    private static readonly IReadOnlyDictionary<string, string> AccentColors =
        new Dictionary<string, string>
        {
            ["Blue"] = "#2563EB",
            ["Green"] = "#16834A",
            ["Purple"] = "#7C3AED",
            ["Teal"] = "#0F766E",
            ["Orange"] = "#C2410C"
        };

    public IReadOnlyList<string> ThemeOptions { get; } = ["Light", "Dark"];

    public IReadOnlyList<string> AccentOptions { get; } = [.. AccentColors.Keys];

    public void ApplyTheme(string theme)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(theme);
        if (!ThemeOptions.Contains(theme, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(theme), theme, "The selected theme is not supported.");
        }

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var currentTheme = dictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);
        if (currentTheme is null)
        {
            throw new InvalidOperationException("The application theme resource dictionary is missing.");
        }

        var source = new Uri($"Themes/{theme}.xaml", UriKind.Relative);
        var replacement = new ResourceDictionary { Source = source };
        var index = dictionaries.IndexOf(currentTheme);
        dictionaries[index] = replacement;
    }

    public void ApplyAccent(string accent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accent);
        if (!AccentColors.TryGetValue(accent, out var color))
        {
            throw new ArgumentOutOfRangeException(nameof(accent), accent, "The selected accent is not supported.");
        }

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        Application.Current.Resources["AccentBrush"] = brush;
        Application.Current.Resources["AccentSoftBrush"] = new SolidColorBrush(
            Color.FromArgb(30, brush.Color.R, brush.Color.G, brush.Color.B));
    }
}
