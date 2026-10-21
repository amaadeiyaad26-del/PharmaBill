using PharmaBill.Core;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class BusinessClockTests
{
    [Fact]
    public void IstIsFivePointFiveHoursAheadOfUtc_WithNoDaylightSaving()
    {
        Assert.Equal(TimeSpan.FromMinutes(330), BusinessClock.TimeZone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(TimeSpan.FromMinutes(330), BusinessClock.TimeZone.GetUtcOffset(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Theory]
    // UTC instant               IST date
    [InlineData("2026-10-20T18:29:59", "2026-10-20")]
    [InlineData("2026-10-20T18:30:00", "2026-10-21")]
    [InlineData("2026-10-21T08:30:00", "2026-10-21")]
    [InlineData("2027-03-31T19:00:00", "2027-04-01")] // 00:30 IST on 1 April
    public void StoredUtcInstants_FallOnTheirIstDate(string utc, string istDate)
    {
        DateTime instant = DateTime.SpecifyKind(DateTime.Parse(utc), DateTimeKind.Unspecified); // as EF returns *AtUtc columns
        Assert.Equal(DateOnly.Parse(istDate), instant.ToBusinessDate());
    }

    [Fact]
    public void IstDayBoundaries_AreConvertedToUtcForQueries()
    {
        Assert.Equal(new DateTime(2026, 10, 20, 18, 30, 0, DateTimeKind.Utc), new DateOnly(2026, 10, 21).StartOfDayUtc());
        Assert.Equal(new DateTime(2027, 3, 31, 19, 0, 0, DateTimeKind.Utc), BusinessClock.FromLocal(new DateTime(2027, 4, 1, 0, 30, 0)));
    }

    [Theory]
    [InlineData("2027-03-31T23:59", "2026-27")]
    [InlineData("2027-04-01T00:30", "2027-28")] // pinned on Android too: 00:30 IST on 1 April is the new year
    [InlineData("2027-04-01T00:00", "2027-28")]
    [InlineData("2027-01-15T12:00", "2026-27")]
    public void FinancialYear_IsDecidedByTheIstDate(string ist, string expected)
    {
        using (BusinessClock.Use(new FixedClock(BusinessClock.FromLocal(DateTime.Parse(ist)))))
        {
            Assert.Equal(expected, BusinessClock.FinancialYear(BusinessClock.Today));
            Assert.Equal(expected, NumberSeriesService.GetFinancialYear(BusinessClock.Today));
        }
    }

    [Fact]
    public void TodayFollowsIst_NotUtcAndNotThePcTimezone()
    {
        // 02:00 IST on 21 Oct is still 20 Oct in UTC.
        using (BusinessClock.Use(new FixedClock(new DateTime(2026, 10, 20, 20, 30, 0, DateTimeKind.Utc))))
        {
            Assert.Equal(new DateOnly(2026, 10, 21), BusinessClock.Today);
            Assert.Equal(new DateTime(2026, 10, 21, 2, 0, 0), BusinessClock.Now);
        }
    }

    [Fact]
    public async Task ClockOverrides_AreIsolatedPerAsyncFlow_AndRestored()
    {
        DateOnly outside = BusinessClock.Today;
        Task<DateOnly> other = Task.Run(async () =>
        {
            using (BusinessClock.Use(new FixedClock(new DateTime(2030, 1, 1, 6, 0, 0, DateTimeKind.Utc))))
            {
                await Task.Delay(50);
                return BusinessClock.Today;
            }
        });

        using (BusinessClock.Use(new FixedClock(new DateTime(2020, 6, 1, 6, 0, 0, DateTimeKind.Utc))))
        {
            await Task.Delay(20);
            Assert.Equal(new DateOnly(2020, 6, 1), BusinessClock.Today);
        }

        Assert.Equal(new DateOnly(2030, 1, 1), await other);
        Assert.Equal(outside, BusinessClock.Today);
    }
}
