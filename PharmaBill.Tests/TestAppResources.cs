using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace PharmaBill.Tests;

/// <summary>
/// Adds the brushes declared directly in App.xaml (outside its merged dictionaries) so page
/// templates that reference them via StaticResource can load without a running App.
/// </summary>
internal static class TestAppResources
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static void AddAppLevelBrushes(ResourceDictionary resources)
    {
        foreach (var brush in LoadAppXaml().Descendants(Wpf + "SolidColorBrush"))
        {
            var key = (string?)brush.Attribute(X + "Key");
            var color = (string?)brush.Attribute("Color");
            if (key is null || color is null)
            {
                continue;
            }

            resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
    }

    private static XDocument LoadAppXaml()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "PharmaBill.App", "App.xaml")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return XDocument.Load(Path.Combine(dir!, "PharmaBill.App", "App.xaml"));
    }
}
