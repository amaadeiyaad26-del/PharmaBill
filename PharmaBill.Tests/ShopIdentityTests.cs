using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PharmaBill.App.Services;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class ShopIdentityTests
{
    [Fact]
    public void Resolve_UsesFirmProfile_AndJoinsAddressAndPhone()
    {
        var profile = new PharmacyProfile { Name = "  Sameer Chemists  ", Address = "12 Main Road\r\nSrinagar", Phone = "9876543210" };

        ShopIdentity identity = ShopIdentityResolver.Resolve(profile, null);

        Assert.Equal("Sameer Chemists", identity.Name);
        Assert.Equal("12 Main Road, Srinagar", identity.Address);
        Assert.Equal("9876543210", identity.Phone);
        Assert.Equal("12 Main Road, Srinagar  ·  9876543210", identity.ContactLine);
        Assert.Null(identity.LogoPng);
    }

    [Fact]
    public void Resolve_ActiveBranchAddressAndPhoneWin_ButNameAndLogoStayWithFirmProfile()
    {
        byte[] logo = [1, 2, 3];
        var profile = new PharmacyProfile { Name = "Sameer Chemists", Address = "Head office road", Phone = "111", LogoPng = logo };
        var branch = new Branch { BranchName = "Lal Chowk branch", Address = "Lal Chowk", ContactPhone = "222" };

        ShopIdentity identity = ShopIdentityResolver.Resolve(profile, branch);

        Assert.Equal("Sameer Chemists", identity.Name);
        Assert.Equal("Lal Chowk", identity.Address);
        Assert.Equal("222", identity.Phone);
        Assert.Same(logo, identity.LogoPng);
    }

    [Fact]
    public void Resolve_BranchWithoutItsOwnDetails_FallsBackToFirmProfile()
    {
        var profile = new PharmacyProfile { Name = "Sameer Chemists", Address = "Main Road", Phone = "111" };
        var branch = new Branch { BranchName = "Main", Address = "  ", ContactPhone = null };

        ShopIdentity identity = ShopIdentityResolver.Resolve(profile, branch);

        Assert.Equal("Main Road", identity.Address);
        Assert.Equal("111", identity.Phone);
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("Main Road", null, "Main Road")]
    [InlineData(null, "111", "111")]
    public void ContactLine_LeavesOutEmptyParts_WithNoPlaceholderText(string? address, string? phone, string expected)
    {
        ShopIdentity identity = ShopIdentityResolver.Resolve(new PharmacyProfile { Name = "Shop", Address = address, Phone = phone, LogoPng = [] }, null);

        Assert.Equal(expected, identity.ContactLine);
        Assert.DoesNotContain("not", identity.ContactLine, StringComparison.OrdinalIgnoreCase);
        Assert.Null(identity.LogoPng);
    }

    [Fact]
    public void Resolve_WithoutAProfileName_UsesANeutralFallback()
    {
        Assert.Equal(ShopIdentityResolver.FallbackName, ShopIdentityResolver.Resolve(null, null).Name);
        Assert.Equal(ShopIdentityResolver.FallbackName, ShopIdentityResolver.Resolve(new PharmacyProfile { Name = " " }, null).Name);
    }

    [Theory]
    [InlineData("Sameer Chemists", "SC")]
    [InlineData("Apollo", "A")]
    [InlineData("M/s Sameer Chemists Pvt Ltd", "SC")]
    [InlineData("The Kashmir Medical Hall", "KM")]
    [InlineData("sri ram medicos & surgicals", "SR")]
    [InlineData("24x7 Pharmacy", "2P")]
    [InlineData("", "P")]
    [InlineData(null, "P")]
    public void Initials_TakeTheFirstTwoSignificantWords(string? name, string expected)
    {
        Assert.Equal(expected, ShopIdentityResolver.Initials(name));
    }

    [Fact]
    public void LogoImporter_ShrinksLargeRasterImages_KeepingTheirShape()
    {
        RunSta(() =>
        {
            foreach (BitmapEncoder encoder in new BitmapEncoder[] { new PngBitmapEncoder(), new JpegBitmapEncoder() })
            {
                string path = WriteImage(1000, 500, encoder);
                try
                {
                    BitmapFrame result = Decode(ShopLogoImporter.ToPng(path));
                    Assert.Equal(ShopLogoImporter.MaxPixels, result.PixelWidth);
                    Assert.Equal(ShopLogoImporter.MaxPixels / 2, result.PixelHeight);
                }
                finally
                {
                    File.Delete(path);
                }
            }
        });
    }

    [Fact]
    public void LogoImporter_NeverEnlargesSmallImages()
    {
        RunSta(() =>
        {
            string path = WriteImage(120, 60, new PngBitmapEncoder());
            try
            {
                BitmapFrame result = Decode(ShopLogoImporter.ToPng(path));
                Assert.Equal((120, 60), (result.PixelWidth, result.PixelHeight));
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void LogoImporter_RendersSvgAtFullSize()
    {
        RunSta(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"logo-{Guid.NewGuid():N}.svg");
            File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"100\" viewBox=\"0 0 200 100\"><rect width=\"200\" height=\"100\" rx=\"12\" fill=\"#4F46E5\"/><circle cx=\"100\" cy=\"50\" r=\"30\" fill=\"#ffffff\"/></svg>");
            try
            {
                BitmapFrame result = Decode(ShopLogoImporter.ToPng(path));
                Assert.Equal(ShopLogoImporter.MaxPixels, result.PixelWidth);
                Assert.InRange(result.PixelHeight, ShopLogoImporter.MaxPixels / 2 - 2, ShopLogoImporter.MaxPixels / 2 + 2);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void LogoImporter_RejectsUnsupportedOrDamagedFiles_WithAPlainMessage()
    {
        RunSta(() =>
        {
            string gif = Path.Combine(Path.GetTempPath(), $"logo-{Guid.NewGuid():N}.gif");
            string png = Path.Combine(Path.GetTempPath(), $"logo-{Guid.NewGuid():N}.png");
            File.WriteAllBytes(gif, [1, 2, 3]);
            File.WriteAllText(png, "not an image");
            try
            {
                Assert.Contains("PNG, JPG or SVG", Assert.Throws<InvalidOperationException>(() => ShopLogoImporter.ToPng(gif)).Message);
                Assert.Contains("could not be read", Assert.Throws<InvalidOperationException>(() => ShopLogoImporter.ToPng(png)).Message);
            }
            finally
            {
                File.Delete(gif);
                File.Delete(png);
            }
        });
    }

    private static string WriteImage(int width, int height, BitmapEncoder encoder)
    {
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.SteelBlue, null, new Rect(0, 0, width, height));
        }

        RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(Path.GetTempPath(), $"logo-{Guid.NewGuid():N}{(encoder is JpegBitmapEncoder ? ".jpg" : ".png")}");
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private static BitmapFrame Decode(byte[] png) =>
        BitmapDecoder.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];

    private static void RunSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        Thread thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
