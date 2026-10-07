using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PharmaBill.Core.Ai;

public static partial class FuzzyMatcher
{
	public const double DefaultThreshold = 0.65;

	[GeneratedRegex("[^a-z0-9 ]+")]
	private static partial Regex NonAlphanumeric();

	[GeneratedRegex("\\d+(?:\\.\\d+)?")]
	private static partial Regex Numbers();

	[GeneratedRegex("^\\d+(?:\\.\\d+)?(?:mg|mcg|g|ml|iu)?$")]
	private static partial Regex StrengthToken();

	public static string Normalize(string? text)
	{
		return string.Join(' ', NonAlphanumeric().Replace((text ?? string.Empty).ToLowerInvariant(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
	}

	public static int Levenshtein(string a, string b)
	{
		if (a.Length == 0)
		{
			return b.Length;
		}
		if (b.Length == 0)
		{
			return a.Length;
		}
		int[] array = new int[b.Length + 1];
		int[] array2 = new int[b.Length + 1];
		for (int i = 0; i <= b.Length; i++)
		{
			array[i] = i;
		}
		for (int j = 1; j <= a.Length; j++)
		{
			array2[0] = j;
			for (int k = 1; k <= b.Length; k++)
			{
				int num = ((a[j - 1] != b[k - 1]) ? 1 : 0);
				array2[k] = Math.Min(Math.Min(array2[k - 1] + 1, array[k] + 1), array[k - 1] + num);
			}
			int[] array3 = array2;
			array2 = array;
			array = array3;
		}
		return array[b.Length];
	}

	public static double Similarity(string? a, string? b)
	{
		string text = Normalize(a);
		string text2 = Normalize(b);
		if (text.Length == 0 || text2.Length == 0)
		{
			return 0.0;
		}
		return 1.0 - (double)Levenshtein(text, text2) / (double)Math.Max(text.Length, text2.Length);
	}

	public static double ScoreName(string? prescribed, string? catalogue)
	{
		string text = Normalize(prescribed);
		string text2 = Normalize(catalogue);
		if (text.Length == 0 || text2.Length == 0)
		{
			return 0.0;
		}
		double num;
		if (text == text2)
		{
			num = 1.0;
		}
		else if (text2.StartsWith(text + " ", StringComparison.Ordinal) || text.StartsWith(text2 + " ", StringComparison.Ordinal))
		{
			num = 0.92;
		}
		else
		{
			string[] array = text.Split(' ');
			string b = string.Join(' ', text2.Split(' ').Take(array.Length));
			num = Math.Max(Similarity(text, text2), Math.Min(0.9, Similarity(text, b)));
		}
		string[] array2 = WordTokens(text);
		string[] fieldWords = WordTokens(text2);
		if (array2.Length != 0 && fieldWords.Length != 0)
		{
			double num2 = Similarity(string.Join(' ', array2), string.Join(' ', fieldWords));
			bool flag = HasStrengthConflict(text, text2);
			num = Math.Max(num, flag ? (num2 * 0.6) : num2);
			if (array2.Length >= 2 && array2.All((string word) => fieldWords.Any((string other) => Similarity(word, other) >= 0.8)))
			{
				return Math.Max(num, 0.8);
			}
		}
		HashSet<string> hashSet = (from match in Numbers().Matches(text)
			select match.Value).ToHashSet();
		HashSet<string> hashSet2 = (from match in Numbers().Matches(text2)
			select match.Value).ToHashSet();
		if (hashSet.Count > 0 && hashSet2.Count > 0 && !hashSet.Overlaps(hashSet2))
		{
			num *= 0.6;
		}
		return num;
	}

	private static string[] WordTokens(string normalized)
	{
		return (from token in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			where !StrengthToken().IsMatch(token)
			select token).ToArray();
	}

	private static bool HasStrengthConflict(string query, string field)
	{
		HashSet<string> hashSet = (from match in Numbers().Matches(query)
			select match.Value).ToHashSet();
		HashSet<string> hashSet2 = (from match in Numbers().Matches(field)
			select match.Value).ToHashSet();
		if (hashSet.Count > 0 && hashSet2.Count > 0)
		{
			return !hashSet.Overlaps(hashSet2);
		}
		return false;
	}

	public static DrugNameMatch? FindBestMatch(string? prescribedName, IEnumerable<DrugNameCandidate> candidates, double threshold = 0.65)
	{
		DrugNameMatch drugNameMatch = null;
		foreach (DrugNameCandidate candidate in candidates)
		{
			string[] array = new string[3] { candidate.Name, candidate.BrandName, candidate.GenericName };
			foreach (string text in array)
			{
				if (!string.IsNullOrWhiteSpace(text))
				{
					double num = ScoreName(prescribedName, text);
					if (num >= threshold && ((object)drugNameMatch == null || num > drugNameMatch.Score))
					{
						drugNameMatch = new DrugNameMatch(candidate.Id, candidate.Name, Math.Round(num, 3));
					}
				}
			}
		}
		return drugNameMatch;
	}
}
