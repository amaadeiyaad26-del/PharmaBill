using System;

namespace PharmaBill.Core;

public static class RetailTaxCalculator
{
	public static InclusiveTaxLine SplitInclusive(decimal quantity, decimal unitPrice, decimal taxRate, decimal discount)
	{
		if (quantity <= 0m)
		{
			throw new ArgumentOutOfRangeException("quantity", "Quantity must be greater than zero.");
		}
		if (unitPrice < 0m || taxRate < 0m || discount < 0m)
		{
			throw new ArgumentOutOfRangeException("unitPrice", "Price, tax rate, and discount cannot be negative.");
		}
		discount = RoundMoney(discount);
		decimal num = RoundMoney(quantity * unitPrice - discount);
		if (num < 0m)
		{
			throw new ArgumentOutOfRangeException("discount", "Discount cannot exceed the GST-inclusive line amount.");
		}
		decimal num2 = RoundMoney(num * taxRate / (100m + taxRate));
		return new InclusiveTaxLine(num, num2, RoundMoney(num - num2));
	}

	public static decimal RoundMoney(decimal amount)
	{
		return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
	}
}
