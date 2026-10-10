using PharmaBill.App.ViewModels;
using Xunit;

namespace PharmaBill.Tests;

public sealed class PurchaseSpreadsheetMappingTests
{
    [Theory]
    [InlineData("07/25 - 06/27", "06/2027")]
    [InlineData("07/26 • 06/27", "06/2027")]
    [InlineData("2027-12-31", "12/2027")]
    public void SpreadsheetExpiry_UsesExpiryDateFromCell(string value, string expected)
    {
        Assert.Equal(expected, PurchasePageViewModel.NormalizeSpreadsheetExpiry(value));
    }

    [Theory]
    [InlineData("12%", true, 12)]
    [InlineData(" 5.5 % ", true, 5.5)]
    [InlineData("12%", false, -1)]
    [InlineData("-", false, 0)]
    [InlineData("—", true, 0)]
    [InlineData("", true, 0)]
    public void OptionalSpreadsheetNumbers_ParseExpectedValues(string value, bool allowPercent, decimal expected)
    {
        bool parsed = PurchasePageViewModel.TryOptionalDecimal(value, allowPercent, out decimal result);

        if (expected < 0m)
        {
            Assert.False(parsed);
            return;
        }

        Assert.True(parsed);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void OptionalSpreadsheetNumbers_RejectMalformedValues()
    {
        Assert.False(PurchasePageViewModel.TryOptionalDecimal("not a number", allowPercent: true, out _));
    }
}
