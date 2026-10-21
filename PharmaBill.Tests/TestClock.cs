using System.Globalization;
using System.Runtime.CompilerServices;
using PharmaBill.Core;

namespace PharmaBill.Tests;

/// <summary>
/// The whole suite runs against a fixed IST moment, never the machine's clock, so it behaves the same at 02:00 and
/// at 14:00. Set PHARMAREMIT_TEST_IST (for example "2026-10-21T02:00") to run it at another moment; individual
/// tests can override the clock for their own flow with BusinessClock.Use.
/// </summary>
internal static class TestClock
{
    public static readonly DateTime DefaultIst = new(2026, 10, 21, 14, 0, 0);

    [ModuleInitializer]
    internal static void Pin()
    {
        string? configured = Environment.GetEnvironmentVariable("PHARMAREMIT_TEST_IST");
        DateTime ist = string.IsNullOrWhiteSpace(configured)
            ? DefaultIst
            : DateTime.ParseExact(configured, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);
        BusinessClock.DefaultClock = new FixedClock(BusinessClock.FromLocal(ist));
    }
}
