using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PharmaBill.App.Services;

public sealed class ThemeService : IThemeService
{
	private static readonly IReadOnlyDictionary<string, string> AccentColors = new Dictionary<string, string>
	{
		["Blue"] = "#2563EB",
		["Green"] = "#16834A",
		["Purple"] = "#7C3AED",
		["Teal"] = "#0F766E",
		["Orange"] = "#C2410C"
	};

	public IReadOnlyList<string> ThemeOptions { get; } = new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "Light", "Dark" });

	public IReadOnlyList<string> AccentOptions { get; }

	public void ApplyTheme(string theme)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(theme, "theme");
		if (!ThemeOptions.Contains(theme, StringComparer.Ordinal))
		{
			throw new ArgumentOutOfRangeException("theme", theme, "The selected theme is not supported.");
		}
		Collection<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
		ResourceDictionary resourceDictionary = mergedDictionaries.FirstOrDefault((ResourceDictionary dictionary) => dictionary.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) ?? false);
		if (resourceDictionary == null)
		{
			throw new InvalidOperationException("The application theme resource dictionary is missing.");
		}
		Uri source = new Uri("Themes/" + theme + ".xaml", UriKind.Relative);
		ResourceDictionary value = new ResourceDictionary
		{
			Source = source
		};
		int index = mergedDictionaries.IndexOf(resourceDictionary);
		mergedDictionaries[index] = value;
	}

	public void ApplyAccent(string accent)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(accent, "accent");
		if (!AccentColors.TryGetValue(accent, out string value))
		{
			throw new ArgumentOutOfRangeException("accent", accent, "The selected accent is not supported.");
		}
		SolidColorBrush solidColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
		solidColorBrush.Freeze();
		Application.Current.Resources["AccentBrush"] = solidColorBrush;
		Application.Current.Resources["AccentSoftBrush"] = new SolidColorBrush(Color.FromArgb(30, solidColorBrush.Color.R, solidColorBrush.Color.G, solidColorBrush.Color.B));
	}

	public ThemeService()
	{
		List<string> list = new List<string>();
		list.AddRange(AccentColors.Keys);
		AccentOptions = new _003C_003Ez__ReadOnlyList<string>(list);
	}
}
