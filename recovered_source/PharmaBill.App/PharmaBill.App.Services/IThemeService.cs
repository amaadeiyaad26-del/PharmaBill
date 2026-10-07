using System.Collections.Generic;

namespace PharmaBill.App.Services;

public interface IThemeService
{
	IReadOnlyList<string> ThemeOptions { get; }

	IReadOnlyList<string> AccentOptions { get; }

	void ApplyTheme(string theme);

	void ApplyAccent(string accent);
}
