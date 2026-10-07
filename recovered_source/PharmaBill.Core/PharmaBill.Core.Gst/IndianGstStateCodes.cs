using System;
using System.Collections.Generic;
using System.Linq;

namespace PharmaBill.Core.Gst;

public static class IndianGstStateCodes
{
	private static readonly Dictionary<string, string> ByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["JAMMU AND KASHMIR"] = "01",
		["JAMMU & KASHMIR"] = "01",
		["HIMACHAL PRADESH"] = "02",
		["PUNJAB"] = "03",
		["CHANDIGARH"] = "04",
		["UTTARAKHAND"] = "05",
		["HARYANA"] = "06",
		["DELHI"] = "07",
		["RAJASTHAN"] = "08",
		["UTTAR PRADESH"] = "09",
		["BIHAR"] = "10",
		["SIKKIM"] = "11",
		["ARUNACHAL PRADESH"] = "12",
		["NAGALAND"] = "13",
		["MANIPUR"] = "14",
		["MIZORAM"] = "15",
		["TRIPURA"] = "16",
		["MEGHALAYA"] = "17",
		["ASSAM"] = "18",
		["WEST BENGAL"] = "19",
		["JHARKHAND"] = "20",
		["ODISHA"] = "21",
		["ORISSA"] = "21",
		["CHHATTISGARH"] = "22",
		["MADHYA PRADESH"] = "23",
		["GUJARAT"] = "24",
		["DADRA AND NAGAR HAVELI AND DAMAN AND DIU"] = "26",
		["DADRA AND NAGAR HAVELI"] = "26",
		["DAMAN AND DIU"] = "26",
		["MAHARASHTRA"] = "27",
		["KARNATAKA"] = "29",
		["GOA"] = "30",
		["LAKSHADWEEP"] = "31",
		["KERALA"] = "32",
		["TAMIL NADU"] = "33",
		["PUDUCHERRY"] = "34",
		["PONDICHERRY"] = "34",
		["ANDAMAN AND NICOBAR ISLANDS"] = "35",
		["TELANGANA"] = "36",
		["ANDHRA PRADESH"] = "37",
		["LADAKH"] = "38",
		["OTHER TERRITORY"] = "97",
		["OTHER COUNTRY"] = "96"
	};

	public static string Resolve(string? stateOrCode, string? fallbackGstin = null)
	{
		if (!string.IsNullOrWhiteSpace(stateOrCode))
		{
			string text = stateOrCode.Trim();
			if (text.Length == 2 && text.All(char.IsDigit))
			{
				return text;
			}
			string key = text.ToUpperInvariant().Replace(".", string.Empty).Replace("  ", " ");
			if (ByName.TryGetValue(key, out string value))
			{
				return value;
			}
		}
		if (!string.IsNullOrWhiteSpace(fallbackGstin) && fallbackGstin.Length >= 2)
		{
			string text2 = GstinValidator.Normalize(fallbackGstin);
			if (text2.Length >= 2)
			{
				return text2.Substring(0, 2);
			}
		}
		return "97";
	}

	public static bool IsSameState(string? supplierState, string? buyerState, string? supplierGstin, string? buyerGstin)
	{
		string a = Resolve(supplierState, supplierGstin);
		string b = Resolve(buyerState, buyerGstin);
		return string.Equals(a, b, StringComparison.Ordinal);
	}
}
