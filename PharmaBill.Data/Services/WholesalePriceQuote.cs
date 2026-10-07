namespace PharmaBill.Data.Services;

public sealed record WholesalePriceQuote(decimal BaseUnitRate, decimal DiscountPercent, decimal UnitRate, decimal FreeQuantity, decimal EffectiveMrp);
