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

        ShopIdentity identity = ShopIdentityResolver.Resolve(profile, null, null);

        Assert.Equal("Sameer Chemists", identity.Name);
        Assert.Equal("12 Main Road, Srinagar", identity.Address);
        Assert.Equal("9876543210", identity.Phone);
        Assert.Equal("12 Main Road, Srinagar  ·  9876543210", identity.ContactLine);
        Assert.Null(identity.LogoPath);
    }

    [Fact]
    public void Resolve_ActiveBranchAddressAndPhoneWin_ButTheNameStaysTheFirmName()
    {
        var profile = new PharmacyProfile { Name = "Sameer Chemists", Address = "Head office road", Phone = "111" };
        var branch = new Branch { BranchName = "Lal Chowk branch", Address = "Lal Chowk", ContactPhone = "222" };

        ShopIdentity identity = ShopIdentityResolver.Resolve(profile, branch, @"C:\logo.png");

        Assert.Equal("Sameer Chemists", identity.Name);
        Assert.Equal("Lal Chowk", identity.Address);
        Assert.Equal("222", identity.Phone);
        Assert.Equal(@"C:\logo.png", identity.LogoPath);
    }

    [Fact]
    public void Resolve_BranchWithoutItsOwnDetails_FallsBackToFirmProfile()
    {
        var profile = new PharmacyProfile { Name = "Sameer Chemists", Address = "Main Road", Phone = "111" };
        var branch = new Branch { BranchName = "Main", Address = "  ", ContactPhone = null };

        ShopIdentity identity = ShopIdentityResolver.Resolve(profile, branch, null);

        Assert.Equal("Main Road", identity.Address);
        Assert.Equal("111", identity.Phone);
    }

    [Theory]
    [InlineData(null, null, "")]
    [InlineData("Main Road", null, "Main Road")]
    [InlineData(null, "111", "111")]
    public void ContactLine_LeavesOutEmptyParts_WithNoPlaceholderText(string? address, string? phone, string expected)
    {
        ShopIdentity identity = ShopIdentityResolver.Resolve(new PharmacyProfile { Name = "Shop", Address = address, Phone = phone }, null, " ");

        Assert.Equal(expected, identity.ContactLine);
        Assert.DoesNotContain("not", identity.ContactLine, StringComparison.OrdinalIgnoreCase);
        Assert.Null(identity.LogoPath);
    }

    [Fact]
    public void Resolve_WithoutAProfileName_UsesANeutralFallback()
    {
        Assert.Equal(ShopIdentityResolver.FallbackName, ShopIdentityResolver.Resolve(null, null, null).Name);
        Assert.Equal(ShopIdentityResolver.FallbackName, ShopIdentityResolver.Resolve(new PharmacyProfile { Name = " " }, null, null).Name);
    }
}
