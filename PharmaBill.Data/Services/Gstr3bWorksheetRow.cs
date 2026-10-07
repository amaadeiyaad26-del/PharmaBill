namespace PharmaBill.Data.Services;

public sealed record Gstr3bWorksheetRow(string Section, string Description, decimal TaxableValue, decimal IntegratedTax, decimal CentralTax, decimal StateTax, decimal Cess);
