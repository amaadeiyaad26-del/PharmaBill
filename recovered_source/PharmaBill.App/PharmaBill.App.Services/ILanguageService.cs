using System;
using System.Collections.Generic;
using System.Windows;

namespace PharmaBill.App.Services;

public interface ILanguageService
{
	IReadOnlyList<string> LanguageOptions { get; }

	FlowDirection FlowDirection { get; }

	event EventHandler? LanguageChanged;

	void ApplyLanguage(string language);

	string GetString(string resourceKey);
}
