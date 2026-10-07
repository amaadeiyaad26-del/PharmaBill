using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace PharmaBill.App.Services;

public sealed class LanguageService : ILanguageService
{
	private static readonly IReadOnlyDictionary<string, string> LanguageDictionaries = new Dictionary<string, string>
	{
		["English"] = "Resources/Strings.en.xaml",
		["Hindi"] = "Resources/Strings.hi.xaml",
		["Urdu"] = "Resources/Strings.ur.xaml"
	};

	public IReadOnlyList<string> LanguageOptions { get; }

	public FlowDirection FlowDirection { get; private set; }

	public event EventHandler? LanguageChanged;

	public void ApplyLanguage(string language)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(language, "language");
		if (!LanguageDictionaries.TryGetValue(language, out string value))
		{
			throw new ArgumentOutOfRangeException("language", language, "The selected language is not supported.");
		}
		Collection<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
		ResourceDictionary resourceDictionary = mergedDictionaries.FirstOrDefault((ResourceDictionary dictionary) => dictionary.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) ?? false);
		if (resourceDictionary == null)
		{
			throw new InvalidOperationException("The application language resource dictionary is missing.");
		}
		ResourceDictionary value2 = new ResourceDictionary
		{
			Source = new Uri(value, UriKind.Relative)
		};
		mergedDictionaries[mergedDictionaries.IndexOf(resourceDictionary)] = value2;
		FlowDirection = ((language == "Urdu") ? FlowDirection.RightToLeft : FlowDirection.LeftToRight);
		LanguageChanged?.Invoke(this, EventArgs.Empty);
	}

	public string GetString(string resourceKey)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey, "resourceKey");
		if (Application.Current.TryFindResource(resourceKey) is string result)
		{
			return result;
		}
		throw new KeyNotFoundException("The localization resource '" + resourceKey + "' was not found.");
	}

	public LanguageService()
	{
		List<string> list = new List<string>();
		list.AddRange(LanguageDictionaries.Keys);
		LanguageOptions = new _003C_003Ez__ReadOnlyList<string>(list);
	}
}
