using System.Reflection;
using QuestPDF.Drawing;

namespace PharmaBill.App.Services;

/// <summary>Registers the bundled Noto Sans (SIL OFL) fonts, which include the rupee glyph, for QuestPDF.</summary>
public static class PdfFonts
{
    private static readonly object Gate = new();
    private static bool _registered;

    public const string FamilyName = "Noto Sans";

    public static string Family
    {
        get
        {
            Register();
            return FamilyName;
        }
    }

    public static void Register()
    {
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                FontManager.RegisterFont(stream);
            }

            _registered = true;
        }
    }
}