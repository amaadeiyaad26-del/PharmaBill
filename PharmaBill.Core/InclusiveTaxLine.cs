namespace PharmaBill.Core;

public readonly record struct InclusiveTaxLine(decimal GrossAmount, decimal TaxAmount, decimal NetAmount);
