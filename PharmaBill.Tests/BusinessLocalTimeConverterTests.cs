using System.Globalization;
using PharmaBill.App.Controls;
using Xunit;

namespace PharmaBill.Tests;

// Register and drug-record screens show stored UTC times as the shop's IST time, as their exports already do.
public sealed class BusinessLocalTimeConverterTests
{
    [Fact]
    public void ShowsUtcAsIst_AcrossMidnight()
    {
        var converter = new BusinessLocalTimeConverter();
        object shown = converter.Convert(new DateTime(2026, 3, 31, 19, 0, 0, DateTimeKind.Utc), typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal("01-Apr-2026 00:30", shown);
        Assert.Equal(string.Empty, converter.Convert(null!, typeof(string), null!, CultureInfo.InvariantCulture));
    }
}
