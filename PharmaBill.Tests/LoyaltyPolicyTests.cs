using PharmaBill.Core.Loyalty;
using Xunit;

namespace PharmaBill.Tests;

public sealed class LoyaltyPolicyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-50, 0)]
    [InlineData(99.99, 0)]
    [InlineData(100, 1)]
    [InlineData(249.50, 2)]
    [InlineData(1000, 10)]
    public void PointsForAmount_EarnsOnePointPerDefaultBlock(decimal bill, int expected) =>
        Assert.Equal(expected, LoyaltyPolicy.PointsForAmount(bill));

    [Fact]
    public void PointsForAmount_UsesCustomBlockSize() =>
        Assert.Equal(4, LoyaltyPolicy.PointsForAmount(200m, amountPerPoint: 50m));

    [Fact]
    public void PointsForAmount_RejectsNonPositiveBlockSize() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LoyaltyPolicy.PointsForAmount(100m, amountPerPoint: 0m));
}
