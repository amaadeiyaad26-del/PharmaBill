using System.Reflection;
using PharmaBill.App.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class MoneyFormatTests
{
    [Fact]
    public void Formats_amount_with_rupee_sign_and_indian_grouping()
    {
        Assert.Equal("\u20B912,34,567.50", MoneyFormat.Rupees(1234567.5m));
        Assert.Equal("\u20B91,23,456.00", MoneyFormat.Rupees(123456m));
        Assert.Equal("\u20B9999.00", MoneyFormat.Rupees(999m));
    }

    [Fact]
    public void Bundled_pdf_fonts_are_embedded_and_register()
    {
        var fonts = typeof(PdfFonts).Assembly.GetManifestResourceNames()
            .Count(name => name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
        Assert.True(fonts >= 2);
        PdfFonts.Register();
        Assert.Equal("Noto Sans", PdfFonts.Family);
    }

    [Fact]
    public void No_source_file_contains_garbled_rupee_text()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !Directory.GetFiles(root, "*.sln*").Any())
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);
        var markers = new[] { "\u00E2\u201A", "\u00E2," };
        var offenders = Directory.EnumerateFiles(root!, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}"))
            .Where(path => new FileInfo(path).Length < 5_000_000)
            .Where(path =>
            {
                var text = File.ReadAllText(path, System.Text.Encoding.UTF8);
                return markers.Any(text.Contains);
            })
            .ToList();
        Assert.Empty(offenders);
    }
}