using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

namespace PharmaBill.Core.Gst;

public static class IndianGstStateCodes
{
	public const string UnresolvedCode = "97";

	public static IReadOnlyList<(string Code, string Name)> States { get; } = new (string Code, string Name)[]
	{
		("01", "Jammu and Kashmir"),
		("02", "Himachal Pradesh"),
		("03", "Punjab"),
		("04", "Chandigarh"),
		("05", "Uttarakhand"),
		("06", "Haryana"),
		("07", "Delhi"),
		("08", "Rajasthan"),
		("09", "Uttar Pradesh"),
		("10", "Bihar"),
		("11", "Sikkim"),
		("12", "Arunachal Pradesh"),
		("13", "Nagaland"),
		("14", "Manipur"),
		("15", "Mizoram"),
		("16", "Tripura"),
		("17", "Meghalaya"),
		("18", "Assam"),
		("19", "West Bengal"),
		("20", "Jharkhand"),
		("21", "Odisha"),
		("22", "Chhattisgarh"),
		("23", "Madhya Pradesh"),
		("24", "Gujarat"),
		("26", "Dadra and Nagar Haveli and Daman and Diu"),
		("27", "Maharashtra"),
		("29", "Karnataka"),
		("30", "Goa"),
		("31", "Lakshadweep"),
		("32", "Kerala"),
		("33", "Tamil Nadu"),
		("34", "Puducherry"),
		("35", "Andaman and Nicobar Islands"),
		("36", "Telangana"),
		("37", "Andhra Pradesh"),
		("38", "Ladakh"),
		("97", "Other Territory"),
		("96", "Other Country")
	};

	private static readonly Dictionary<string, string> ByName = BuildByName();

	private static readonly HashSet<string> KnownCodes = States.Select(s => s.Code).ToHashSet(StringComparer.Ordinal);

	private static Dictionary<string, string> BuildByName()
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach ((string code, string name) in States)
		{
			map[Normalize(name)] = code;
		}
		(string Alias, string Code)[] aliases =
		{
			("J AND K", "01"),
			("JK", "01"),
			("NCT OF DELHI", "07"),
			("NEW DELHI", "07"),
			("ORISSA", "21"),
			("DADRA AND NAGAR HAVELI", "26"),
			("DAMAN AND DIU", "26"),
			("PONDICHERRY", "34"),
			("ANDAMAN AND NICOBAR", "35")
		};
		foreach ((string alias, string code) in aliases)
		{
			map[Normalize(alias)] = code;
		}
		return map;
	}

	private static string Normalize(string text)
	{
		string upper = text.ToUpperInvariant().Replace(".", string.Empty).Replace("&", " AND ");
		return Regex.Replace(upper, "\\s+", " ").Trim();
	}

	private static bool TryResolveState(string stateOrCode, [NotNullWhen(true)] out string? code)
	{
		string text = stateOrCode.Trim();
		if (text.Length == 2 && text.All(char.IsDigit))
		{
			code = KnownCodes.Contains(text) ? text : null;
			return code != null;
		}
		return ByName.TryGetValue(Normalize(text), out code);
	}

	private static bool TryResolveGstin(string? gstin, [NotNullWhen(true)] out string? code)
	{
		code = null;
		if (string.IsNullOrWhiteSpace(gstin))
		{
			return false;
		}
		string normalized = GstinValidator.Normalize(gstin);
		if (normalized.Length < 2)
		{
			return false;
		}
		string prefix = normalized.Substring(0, 2);
		if (!prefix.All(char.IsDigit) || !KnownCodes.Contains(prefix))
		{
			return false;
		}
		code = prefix;
		return true;
	}

	/// <summary>Lenient lookup for reports: state first, then GSTIN prefix, else "97".</summary>
	public static string Resolve(string? stateOrCode, string? fallbackGstin = null)
	{
		if (!string.IsNullOrWhiteSpace(stateOrCode) && TryResolveState(stateOrCode, out string? fromState))
		{
			return fromState;
		}
		if (TryResolveGstin(fallbackGstin, out string? fromGstin))
		{
			return fromGstin;
		}
		return UnresolvedCode;
	}

	/// <summary>
	/// Strict lookup for tax decisions. A non-blank state must be recognised on its own (no GSTIN guess
	/// behind a typo); a blank state falls back to the GSTIN prefix. Returns false when neither resolves.
	/// </summary>
	public static bool TryResolve(string? stateOrCode, string? gstin, [NotNullWhen(true)] out string? code)
	{
		if (!string.IsNullOrWhiteSpace(stateOrCode))
		{
			return TryResolveState(stateOrCode, out code);
		}
		return TryResolveGstin(gstin, out code);
	}

	public static string? NameForCode(string? code)
	{
		if (string.IsNullOrWhiteSpace(code))
		{
			return null;
		}
		foreach ((string c, string name) in States)
		{
			if (c == code)
			{
				return name;
			}
		}
		return null;
	}

	/// <summary>Canonical state name derived from a GSTIN's first two digits, or null.</summary>
	public static string? StateNameFromGstin(string? gstin)
	{
		return TryResolveGstin(gstin, out string? code) ? NameForCode(code) : null;
	}

	public static bool IsSameState(string? supplierState, string? buyerState, string? supplierGstin, string? buyerGstin)
	{
		string a = Resolve(supplierState, supplierGstin);
		string b = Resolve(buyerState, buyerGstin);
		return string.Equals(a, b, StringComparison.Ordinal);
	}
}
