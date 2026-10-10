using PharmaBill.App.Services;
using Xunit;

namespace PharmaBill.Tests;

public class InvoiceTextParserTests
{
	private const string SampleColumnarInvoice = """
		Seller (Wholesaler)
		M/s Example Pharma Distributors
		Lal Chowk Road, Srinagar.
		Drug Licence No.: DUMMY-WS-0001 (Form 20-B / 21-B)
		GSTIN: 01AAAA0000A1Z5
		TAX INVOICE / CASH-CREDIT MEMO
		Buyer (Retailer)
		M/s Sample Medical Hall
		Rajbagh, Srinagar.
		Invoice No.: KPD/2026-27/0148
		Date: 08/10/2026
		Product
		Paracetamol Tablets IP 500 mg
		Amoxicillin 500 mg + Clavulanic Acid 125 mg
		Pantoprazole Gastro-resistant Tablets IP 40 mg
		Cetirizine Tablets IP 10 mg
		Azithromycin Tablets IP 500 mg
		Metformin Hydrochloride SR Tablets IP 500 mg
		Manufacturer
		Abc Ltd.
		Xyz Remedies Pvt. Ltd.
		Ace Labs Ltd.
		Demo Healthcare
		Xyz Pvt. Ltd.
		Demo Labs
		Pack
		10x10 strip
		10x10 strip
		1x10 strip
		10x10 strip
		1x10 strip
		10x10 strip
		Batch No
		PCT2407
		AMC2512
		PAN2509
		CET2601
		AZI2603
		MET2511
		Mfg / Exp
		07/25 - 06/27
		12/25 - 11/27
		09/25 - 08/27
		01/26 - 12/27
		03/26 - 02/28
		11/25 - 10/27
		Qty
		20
		100
		60
		80
		50
		100
		Rate (Rs.)
		180.00
		145.00
		62.00
		18.50
		98.00
		21.00
		Amount (Rs.)
		3600.00
		14500.00
		3720.00
		1480.00
		4900.00
		2100.00
		Taxable value
		Rs. 30,300.00
		CGST @ 2.5%
		SGST @ 2.5%
		Grand total
		Rs. 31,815.00
		Amount in words: Thirty One Thousand Eight Hundred Fifteen only.
		Authorised Signatory
		""";

	[Fact]
	public void Parse_ColumnarSample_ExtractsSellerAndSixMedicines()
	{
		var invoice = InvoiceTextParser.Parse(SampleColumnarInvoice, "sample");

		Assert.Contains("Example Pharma Distributors", invoice.Supplier, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("Sample Medical Hall", invoice.Supplier, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(6, invoice.Items.Count);

		Assert.Contains(invoice.Items, i => i.ItemName.Contains("Paracetamol", StringComparison.OrdinalIgnoreCase) && i.Batch == "PCT2407" && i.Quantity == 20m && i.Rate == 180m);
		Assert.Contains(invoice.Items, i => i.ItemName.Contains("Amoxicillin", StringComparison.OrdinalIgnoreCase) && i.Batch == "AMC2512" && i.Quantity == 100m && i.Rate == 145m);
		Assert.Contains(invoice.Items, i => i.ItemName.Contains("Pantoprazole", StringComparison.OrdinalIgnoreCase) && i.Batch == "PAN2509" && i.Quantity == 60m && i.Rate == 62m);
		Assert.Contains(invoice.Items, i => i.ItemName.Contains("Cetirizine", StringComparison.OrdinalIgnoreCase) && i.Batch == "CET2601" && i.Quantity == 80m && i.Rate == 18.50m);
		Assert.Contains(invoice.Items, i => i.ItemName.Contains("Azithromycin", StringComparison.OrdinalIgnoreCase) && i.Batch == "AZI2603" && i.Quantity == 50m && i.Rate == 98m);
		Assert.Contains(invoice.Items, i => i.ItemName.Contains("Metformin", StringComparison.OrdinalIgnoreCase) && i.Batch == "MET2511" && i.Quantity == 100m && i.Rate == 21m);

		var amox = invoice.Items.First(i => i.ItemName.Contains("Amoxicillin", StringComparison.OrdinalIgnoreCase));
		Assert.NotEqual(125m, amox.Quantity);
		Assert.NotEqual(500m, amox.Quantity);
		Assert.Equal("11/2027", amox.Expiry);
	}

	[Fact]
	public void StripDosageStrengths_RemovesMgTokens()
	{
		string cleaned = InvoiceTextParser.StripDosageStrengths("Amoxicillin 500 mg + Clavulanic Acid 125 mg");
		Assert.DoesNotContain("500", cleaned);
		Assert.DoesNotContain("125", cleaned);
	}

	[Fact]
	public void LooksLikeMedicineProduct_RejectsStrengthOnlyFragments()
	{
		Assert.False(InvoiceTextParser.LooksLikeMedicineProduct("5m 125 mg"));
		Assert.True(InvoiceTextParser.LooksLikeMedicineProduct("Amoxicillin 500 mg + Clavulanic Acid 125 mg"));
		Assert.True(InvoiceTextParser.HasPlausibleBatchToken("PCT2407"));
	}

	[Fact]
	public void ExtractExpiry_UsesSecondDateInComposite()
	{
		Assert.Equal("06/2027", InvoiceTextParser.ExtractExpiry("07/25 - 06/27"));
		Assert.Equal("06/2027", InvoiceTextParser.ExtractExpiry("07/26 • 06/27"));
	}
}

