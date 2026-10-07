using System;
using System.Linq;

namespace PharmaBill.Core.Gst;

public static class GstinValidator
{
	private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

	public static bool IsValid(string? gstin)
	{
		if (string.IsNullOrWhiteSpace(gstin))
		{
			return false;
		}
		string text = Normalize(gstin);
		if (text.Length != 15)
		{
			return false;
		}
		for (int i = 0; i < 15; i++)
		{
			if ("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(text[i]) < 0)
			{
				return false;
			}
		}
		if (!char.IsDigit(text[0]) || !char.IsDigit(text[1]))
		{
			return false;
		}
		return text[14] == ComputeCheckDigit(text.Substring(0, 14));
	}

	public static string Normalize(string gstin)
	{
		return new string(gstin.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
	}

	public static char ComputeCheckDigit(string firstFourteen)
	{
		if (firstFourteen.Length != 14)
		{
			throw new ArgumentException("GSTIN body must be 14 characters before the check digit.", "firstFourteen");
		}
		int num = 1;
		int num2 = 0;
		foreach (char value in firstFourteen)
		{
			int num3 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ".IndexOf(value);
			if (num3 < 0)
			{
				throw new ArgumentException($"Invalid GSTIN character '{value}'.", "firstFourteen");
			}
			int num4 = num3 * num;
			num2 += num4 / 36 + num4 % 36;
			num = ((num != 1) ? 1 : 2);
		}
		int index = (36 - num2 % 36) % 36;
		return "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[index];
	}
}
