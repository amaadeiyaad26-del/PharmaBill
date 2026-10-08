using PharmaBill.App.Services;
using Xunit;

namespace PharmaBill.Tests;

public class InvoiceTextParserTests
{
	[Fact]
	public void Parses_dummy_wholesaler_invoice_rows_and_header()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Dummy_Invoice_Wholesaler_to_Retailer.pdf");
		if (!File.Exists(path))
		{
			return;
		}
		string text = InvoiceDocumentExtractor.ReadEmbeddedText(path);
		var invoice = InvoiceTextParser.Parse(text, "Dummy_Invoice_Wholesaler_to_Retailer");
		Assert.Equal("Example Pharma Distributors", invoice.Supplier);
		Assert.Equal("KPD/2026-27/0148", invoice.InvoiceNo);
		Assert.Equal("2026-10-08", invoice.InvoiceDate);
		Assert.Equal(30300m, invoice.Subtotal);
		Assert.Equal(31815m, invoice.GrandTotal);
		Assert.Equal(6, invoice.Items.Count);
		Assert.Equal(("PCT2407", "06/2027", 20m, 180.00m), (invoice.Items[0].Batch, invoice.Items[0].Expiry, invoice.Items[0].Quantity, invoice.Items[0].Rate));
		Assert.Equal(("AMC2512", "11/2027", 100m, 145.00m), (invoice.Items[1].Batch, invoice.Items[1].Expiry, invoice.Items[1].Quantity, invoice.Items[1].Rate));
		Assert.Equal(("PAN2509", "08/2027", 60m, 62.00m), (invoice.Items[2].Batch, invoice.Items[2].Expiry, invoice.Items[2].Quantity, invoice.Items[2].Rate));
		Assert.Equal(("CET2601", "12/2027", 80m, 18.50m), (invoice.Items[3].Batch, invoice.Items[3].Expiry, invoice.Items[3].Quantity, invoice.Items[3].Rate));
		Assert.Equal(("AZI2603", "02/2028", 50m, 98.00m), (invoice.Items[4].Batch, invoice.Items[4].Expiry, invoice.Items[4].Quantity, invoice.Items[4].Rate));
		Assert.Equal(("MET2511", "10/2027", 100m, 21.00m), (invoice.Items[5].Batch, invoice.Items[5].Expiry, invoice.Items[5].Quantity, invoice.Items[5].Rate));
		Assert.Contains("Paracetamol", invoice.Items[0].ItemName);
		Assert.Contains("Amoxicillin", invoice.Items[1].ItemName);
		Assert.Contains("Clavulanic", invoice.Items[1].ItemName);
		Assert.Contains("Pantoprazole", invoice.Items[2].ItemName);
		Assert.Contains("Cetirizine", invoice.Items[3].ItemName);
		Assert.Contains("Azithromycin", invoice.Items[4].ItemName);
		Assert.Contains("Metformin", invoice.Items[5].ItemName);
		Assert.All(invoice.Items, item => Assert.Equal(5m, item.Gst));
	}
}
