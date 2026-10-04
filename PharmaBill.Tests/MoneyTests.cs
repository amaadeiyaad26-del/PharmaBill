using Xunit;

namespace PharmaBill.Tests;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(1.25, 125)]
    [InlineData(0.005, 1)]
    [InlineData(-0.005, -1)]
    public void ToPaise_RoundsHalfAwayFromZero(decimal amount, long expected) =>
        Assert.Equal(expected, PharmaBill.Core.Money.ToPaise(amount));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(125, 1.25)]
    [InlineData(-12, -0.12)]
    public void FromPaise_ConvertsToDecimal(long paise, decimal expected) =>
        Assert.Equal(expected, PharmaBill.Core.Money.FromPaise(paise));
}
