using System.Linq;
using System.Text;
using PharmaBill.Core;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

public static class ThermalReceiptFormatter
{
	public static string Format(RetailBillPrintData bill, DocumentOutputSettings settings)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(Center(bill.PharmacyName, 32));
		AppendLineIfPresent(stringBuilder, bill.PharmacyAddress);
		AppendLineIfPresent(stringBuilder, bill.AddressLine2);
		AppendLineIfPresent(stringBuilder, (bill.PharmacyPhone == null) ? null : ("Ph: " + bill.PharmacyPhone));
		AppendLineIfPresent(stringBuilder, (bill.Gstin == null) ? null : ("GSTIN " + bill.Gstin));
		foreach (string licenceNumber in bill.LicenceNumbers)
		{
			stringBuilder.AppendLine(Ascii("DL " + licenceNumber));
		}
		AppendLineIfPresent(stringBuilder, string.IsNullOrWhiteSpace(bill.FssaiNumber) ? null : ("FSSAI " + bill.FssaiNumber));
		stringBuilder.AppendLine(new string('-', 32));
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder3 = stringBuilder2;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("BILL ");
		handler.AppendFormatted(bill.InvoiceNo);
		stringBuilder3.AppendLine(ref handler);
		stringBuilder.AppendLine(bill.SaleAtUtc.ToLocalTime().ToString("dd-MMM-yyyy HH:mm"));
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder4 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("Patient: ");
		handler.AppendFormatted(Ascii(bill.PatientName));
		stringBuilder4.AppendLine(ref handler);
		if (settings.PrintCustomerPhone && !string.IsNullOrWhiteSpace(bill.PatientPhone))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder5 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
			handler.AppendLiteral("Phone: ");
			handler.AppendFormatted(bill.PatientPhone);
			stringBuilder5.AppendLine(ref handler);
		}
		AppendLineIfPresent(stringBuilder, string.IsNullOrWhiteSpace(bill.MrdNumber) ? null : ("MRD / IPD No: " + Ascii(bill.MrdNumber)));
		if (settings.PrintDoctorName && !string.IsNullOrWhiteSpace(bill.PrescriberName))
		{
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(4, 1, stringBuilder2);
			handler.AppendLiteral("Dr: ");
			handler.AppendFormatted(Ascii(bill.PrescriberName));
			stringBuilder6.AppendLine(ref handler);
			AppendLineIfPresent(stringBuilder, (bill.PrescriberRegistrationNumber == null) ? null : ("Reg: " + bill.PrescriberRegistrationNumber));
		}
		stringBuilder.AppendLine(new string('-', 32));
		foreach (RetailBillPrintLine item in bill.Items)
		{
			stringBuilder.AppendLine(Ascii(item.DrugName));
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(5, 2, stringBuilder2);
			handler.AppendLiteral("B:");
			handler.AppendFormatted(Ascii(item.BatchNo));
			handler.AppendLiteral(" E:");
			handler.AppendFormatted(item.ExpiryDate?.ToString("MM/yy") ?? "--");
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(6, 3, stringBuilder2);
			handler.AppendFormatted(item.Quantity, "0.##");
			handler.AppendLiteral(" x ");
			handler.AppendFormatted(item.UnitPrice, "N2");
			handler.AppendLiteral(" = ");
			handler.AppendFormatted(item.LineTotal, "N2");
			stringBuilder8.AppendLine(ref handler);
		}
		stringBuilder.AppendLine(new string('-', 32));
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder9 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(9, 1, stringBuilder2);
		handler.AppendLiteral("Taxable: ");
		handler.AppendFormatted(bill.Subtotal, "N2");
		stringBuilder9.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder10 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(14, 2, stringBuilder2);
		handler.AppendLiteral("CGST: ");
		handler.AppendFormatted(bill.CgstAmount, "N2");
		handler.AppendLiteral("  SGST: ");
		handler.AppendFormatted(bill.SgstAmount, "N2");
		stringBuilder10.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder11 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(5, 1, stringBuilder2);
		handler.AppendLiteral("GST: ");
		handler.AppendFormatted(bill.TaxAmount, "N2");
		stringBuilder11.AppendLine(ref handler);
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder12 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(7, 1, stringBuilder2);
		handler.AppendLiteral("TOTAL: ");
		handler.AppendFormatted(bill.TotalAmount, "N2");
		stringBuilder12.AppendLine(ref handler);
		stringBuilder.AppendLine(Ascii(IndianNumberWords.Convert(bill.TotalAmount)));
		AppendLineIfPresent(stringBuilder, bill.PharmacistName);
		stringBuilder.AppendLine("Sign: ______________");
		if (settings.PrintTermsDisclaimer && !string.IsNullOrWhiteSpace(bill.TermsText))
		{
			stringBuilder.AppendLine(new string('-', 32));
			stringBuilder.AppendLine(Ascii(bill.TermsText));
		}
		AppendLineIfPresent(stringBuilder, settings.FooterText);
		stringBuilder.AppendLine();
		stringBuilder2 = stringBuilder;
		StringBuilder stringBuilder13 = stringBuilder2;
		handler = new StringBuilder.AppendInterpolatedStringHandler(3, 1, stringBuilder2);
		handler.AppendLiteral("QR:");
		handler.AppendFormatted(bill.InvoiceNo);
		stringBuilder13.AppendLine(ref handler);
		return stringBuilder.ToString();
	}

	private static string Center(string text, int width)
	{
		text = Ascii(text);
		if (text.Length >= width)
		{
			return text;
		}
		int count = (width - text.Length) / 2;
		return new string(' ', count) + text;
	}

	private static void AppendLineIfPresent(StringBuilder builder, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			builder.AppendLine(Ascii(value));
		}
	}

	private static string Ascii(string value)
	{
		return new string(value.Select((char character) => (character > '\u007f') ? '?' : character).ToArray());
	}
}
