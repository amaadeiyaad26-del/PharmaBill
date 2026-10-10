using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace PharmaBill.App.Services;

/// <summary>
/// Gives Wholesale B2B its own accent colour so it can't be mistaken for Retail Counter.
/// Retail keeps whatever accent the user chose; the previous brushes are saved on entry and restored on exit.
/// </summary>
public static class BillingModeAccent
{
	private static readonly string[] Keys = { "AccentBrush", "AccentHoverBrush", "AccentSoftBrush" };

	private static Brush[]? _savedWhileWholesale;

	public static void Apply(ActiveBillingDeskMode mode)
	{
		Application? app = Application.Current;
		if (app == null)
		{
			return;
		}

		if (mode == ActiveBillingDeskMode.Wholesale)
		{
			if (_savedWhileWholesale == null)
			{
				_savedWhileWholesale = Keys.Select(key => app.TryFindResource(key) as Brush ?? Brushes.Transparent).ToArray();
			}
			bool dark = IsDarkTheme(app);
			SetAccent(app, dark ? "#FFFBBF24" : "#FFB45309", dark ? "#FFF59E0B" : "#FF92400E", dark ? "#26FBBF24" : "#1EB45309");
			return;
		}

		if (_savedWhileWholesale != null)
		{
			for (int i = 0; i < Keys.Length; i++)
			{
				app.Resources[Keys[i]] = _savedWhileWholesale[i];
			}
			_savedWhileWholesale = null;
		}
	}

	private static void SetAccent(Application app, string accent, string hover, string soft)
	{
		app.Resources["AccentBrush"] = Frozen(accent);
		app.Resources["AccentHoverBrush"] = Frozen(hover);
		app.Resources["AccentSoftBrush"] = Frozen(soft);
	}

	private static SolidColorBrush Frozen(string hex)
	{
		SolidColorBrush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
		brush.Freeze();
		return brush;
	}

	private static bool IsDarkTheme(Application app)
	{
		if (app.TryFindResource("WindowBackgroundBrush") is not SolidColorBrush background)
		{
			return false;
		}
		Color c = background.Color;
		return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128;
	}
}
