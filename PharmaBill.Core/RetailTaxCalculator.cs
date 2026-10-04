namespace PharmaBill.Core;

public readonly record struct InclusiveTaxLine(decimal GrossAmount, decimal TaxAmount, decimal NetAmount);

public static class RetailTaxCalculator
{
    public static InclusiveTaxLine SplitInclusive(decimal quantity, decimal unitPrice, decimal taxRate, decimal discount)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0 || taxRate < 0 || discount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Price, tax rate, and discount cannot be negative.");
        }

        discount = RoundMoney(discount);
        var gross = RoundMoney(quantity * unitPrice - discount);
        if (gross < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(discount), "Discount cannot exceed the GST-inclusive line amount.");
        }

        var tax = RoundMoney(gross * taxRate / (100m + taxRate));
        return new InclusiveTaxLine(gross, tax, RoundMoney(gross - tax));
    }

    public static decimal RoundMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
}
