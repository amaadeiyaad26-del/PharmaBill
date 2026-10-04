namespace PharmaBill.Core;

public static class Money
{
    public static long ToPaise(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    public static decimal FromPaise(long paise) => paise / 100m;
}
