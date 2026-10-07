using System;
using System.CodeDom.Compiler;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.RegularExpressions.Generated;

namespace PharmaBill.App.Services;

public static class UpiPaymentPayload
{
	public static bool IsValidVpa(string? upiVpa)
	{
		if (string.IsNullOrWhiteSpace(upiVpa))
		{
			return false;
		}
		return VpaRegex().IsMatch(upiVpa.Trim());
	}

	public static string Build(string upiVpa, string pharmacyName, decimal amountInr, string invoiceNo)
	{
		if (string.IsNullOrWhiteSpace(upiVpa))
		{
			throw new ArgumentException("UPI VPA is required.", "upiVpa");
		}
		if (!IsValidVpa(upiVpa))
		{
			throw new ArgumentException("UPI VPA must look like username@bank (e.g. store@upi).", "upiVpa");
		}
		string text = Math.Round(amountInr, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
		return "upi://pay?pa=" + Uri.EscapeDataString(upiVpa.Trim()) + "&pn=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(pharmacyName) ? "Pharmacy" : pharmacyName.Trim()) + "&am=" + text + "&tr=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(invoiceNo) ? "BILL" : invoiceNo.Trim()) + "&cu=INR";
	}

	[GeneratedRegex("^[A-Za-z0-9._-]{2,256}@[A-Za-z][A-Za-z0-9.-]{1,64}$", RegexOptions.CultureInvariant)]
	[GeneratedCode("System.Text.RegularExpressions.Generator", "10.0.14.42308")]
	private static Regex VpaRegex()
	{
		return _003CRegexGenerator_g_003EF9618A1D94440A008747A921DF07B7A90AC6239C6D02BDF067036B1F308D3A883__VpaRegex_0.Instance;
	}

	public static string NormalizeMethod(string? displayMethod)
	{
		string text = (displayMethod ?? string.Empty).Trim();
		switch (text)
		{
		case "UPI / QR":
		case "UPI / Dynamic QR":
		case "UPI":
			return "UPI";
		case "Card":
		case "Card / POS":
		case "Card / POS Terminal":
			return "Card";
		case "Cheque":
		case "Cheque / Bank Transfer":
			return "Cheque";
		case "Credit":
		case "Credit / Ledger":
			return "Credit";
		case "Split Payment":
			return "Split";
		case "Cash":
			return "Cash";
		default:
			return text;
		}
	}

	public static bool IsUpi(string? displayMethod)
	{
		return NormalizeMethod(displayMethod) == "UPI";
	}

	public static bool IsCash(string? displayMethod)
	{
		return NormalizeMethod(displayMethod) == "Cash";
	}

	public static bool IsCard(string? displayMethod)
	{
		return NormalizeMethod(displayMethod) == "Card";
	}

	public static bool IsCheque(string? displayMethod)
	{
		return NormalizeMethod(displayMethod) == "Cheque";
	}

	public static bool IsCredit(string? displayMethod)
	{
		return NormalizeMethod(displayMethod) == "Credit";
	}

	public static bool IsSplit(string? displayMethod)
	{
		return NormalizeMethod(displayMethod) == "Split";
	}
}
