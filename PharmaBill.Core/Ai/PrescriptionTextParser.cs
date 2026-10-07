using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PharmaBill.Core.Ai;

public static partial class PrescriptionTextParser
{
	private static readonly HashSet<string> NameStopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"reg", "regd", "registration", "no", "mbbs", "md", "ms", "dnb", "bds", "bams",
		"bhms", "dm", "mch", "date", "dt", "patient", "pt", "name", "phone", "ph",
		"tel", "mob", "mobile", "age", "sex", "gender", "clinic", "hospital", "address", "rx",
		"years", "yrs", "male", "female", "consultant", "physician"
	};

	private static readonly string[] DateFormats = new string[17]
	{
		"d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d M yyyy", "d/M/yy", "d-M-yy", "d.M.yy", "d M yy", "d MMM yyyy", "d MMMM yyyy",
		"d-MMM-yyyy", "d/MMM/yyyy", "d.MMM.yyyy", "d MMM yy", "d-MMM-yy", "dMMMyyyy", "dMMMMyyyy"
	};

	[GeneratedRegex("\\bDr\\.?[ \\t]+(?<n>[A-Za-z][A-Za-z.'\\-]*(?:[ \\t]+[A-Za-z][A-Za-z.'\\-]*){0,3})", RegexOptions.IgnoreCase)]
	private static partial Regex DoctorRegex();

	[GeneratedRegex("\\bReg(?:d|istration)?\\.?[ \\t]*(?:No\\.?|Number|#)?[ \\t]*[:\\-]?[ \\t]*(?<r>[A-Z]{0,5}[ \\-/]?\\d[A-Z0-9\\-/]{2,18})", RegexOptions.IgnoreCase)]
	private static partial Regex RegistrationRegex();

	[GeneratedRegex("(?:\\b(?:Pati?en?t|Patin?et|Patei?nt|Pt)(?:'s)?\\b\\.?(?:[ \\t]+name\\b)?|\\bName\\b)[ \\t]*[:\\-.]*[ \\t]*(?<n>[A-Za-z][A-Za-z.'\\-]*(?:[ \\t]+[A-Za-z][A-Za-z.'\\-]*){0,3})", RegexOptions.IgnoreCase)]
	private static partial Regex PatientRegex();

	[GeneratedRegex("\\b(?:Date|Dt)\\b[ \\t]*[:.\\-]?[ \\t]*(?<d>\\d{1,2}[ /.\\-]\\d{1,2}[ /.\\-]\\d{2,4}|\\d{1,2}[ \\-/]?[A-Za-z]{3,9}[ ,.\\-/]*\\d{2,4})", RegexOptions.IgnoreCase)]
	private static partial Regex LabelledDateRegex();

	[GeneratedRegex("(?<!\\d)\\d{1,2}[/.\\-]\\d{1,2}[/.\\-]\\d{2,4}(?!\\d)")]
	private static partial Regex AnyDateRegex();

	[GeneratedRegex("^(?:\\d{1,2}\\s*[.)\\-]\\s*)?(?:rx\\s*[:.]?\\s*)?(?<form>tab(?:let)?s?|cap(?:sule)?s?|syp|syrup|inj(?:ection)?|oint(?:ment)?|cream|gel|drops?|susp(?:ension)?|lotion|sachet|powder|spray|inhaler)\\b\\.?\\s*(?<rest>.+)$", RegexOptions.IgnoreCase)]
	private static partial Regex FormPrefixRegex();

	[GeneratedRegex("(?<![\\d.])\\d+(?:\\.\\d+)?\\s*(?:mg|mcg|g|ml|iu)\\b", RegexOptions.IgnoreCase)]
	private static partial Regex StrengthRegex();

	[GeneratedRegex("(?<![\\d.])(?<tri>\\d(?:\\.\\d)?\\s*[-\u2013]\\s*\\d(?:\\.\\d)?\\s*[-\u2013]\\s*\\d(?:\\.\\d)?(?:\\s*[-\u2013]\\s*\\d(?:\\.\\d)?)?)(?![\\d.])|\\b(?<abbr>OD|BD|BID|TDS|TID|QID|QD|HS|SOS|STAT|once\\s+daily|twice\\s+daily|thrice\\s+daily|\\d\\s*times?\\s*(?:a\\s*)?(?:day|daily))\\b", RegexOptions.IgnoreCase)]
	private static partial Regex DosageRegex();

	[GeneratedRegex("(?:\\b(?:x|for)\\s*)?(?<![\\d.\\-])(?<n>\\d{1,3})\\s*(?<unit>days?|d|weeks?|wks?|months?)\\b", RegexOptions.IgnoreCase)]
	private static partial Regex DurationRegex();

	[GeneratedRegex("\\b(?:dr|reg|regd|registration|patient|date|phone|mob|mobile|address|clinic|hospital|age|name)\\b\\s*[.:]", RegexOptions.IgnoreCase)]
	private static partial Regex HeaderLineRegex();

	[GeneratedRegex("^\\s*(?:R/?x|℞|Rx)\\b[ \\t]*[:.\\-]?[ \\t]*(?<rest>.*)$", RegexOptions.IgnoreCase)]
	private static partial Regex RxLineRegex();

	public static PrescriptionParseResultDto Parse(string? text)
	{
		string text2 = text ?? string.Empty;
		List<PrescribedItemDto> list = ParseRxSection(text2);
		if (list.Count == 0)
		{
			string[] array = text2.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				if (TryParseMedicineLine(array[i].Trim(), out PrescribedItemDto item))
				{
					list.Add(item);
				}
			}
		}
		return new PrescriptionParseResultDto
		{
			DoctorName = FindDoctor(text2),
			DoctorRegistrationNo = FindRegistration(text2),
			PatientName = FindPatient(text2),
			PrescriptionDate = FindDate(text2),
			DetectedMedicines = list,
			RawExtractedText = text2,
			Source = "Local OCR"
		};
	}

	private static List<PrescribedItemDto> ParseRxSection(string raw)
	{
		List<PrescribedItemDto> list = new List<PrescribedItemDto>();
		bool flag = false;
		string[] array = raw.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length == 0)
			{
				continue;
			}
			if (!flag)
			{
				Match match = RxLineRegex().Match(text);
				if (!match.Success)
				{
					continue;
				}
				flag = true;
				text = match.Groups["rest"].Value.Trim();
				if (text.Length == 0)
				{
					continue;
				}
			}
			if (AnyDateRegex().IsMatch(text) || LabelledDateRegex().IsMatch(text))
			{
				break;
			}
			if (TryParseRxCandidate(text, out PrescribedItemDto item))
			{
				list.Add(item);
			}
		}
		return list;
	}

	private static bool TryParseRxCandidate(string line, out PrescribedItemDto item)
	{
		item = new PrescribedItemDto();
		string text = Regex.Replace(line, "^\\s*(?:\\d{1,2}\\s*[.)]\\s*|[-•*]\\s*)", string.Empty);
		if (HeaderLineRegex().IsMatch(text) || Regex.IsMatch(text, "^(?:sign|signature|advice|follow|review|diagnosis|dx|c/o)\\b", RegexOptions.IgnoreCase))
		{
			return false;
		}
		Match match = FormPrefixRegex().Match(text);
		string text2 = (match.Success ? match.Groups["rest"].Value : text);
		Match match2 = DosageRegex().Match(text2);
		Match match3 = DurationRegex().Match(text2);
		int num = text2.Length;
		if (match2.Success)
		{
			num = Math.Min(num, match2.Index);
		}
		if (match3.Success)
		{
			num = Math.Min(num, match3.Index);
		}
		string text3 = Regex.Replace(text2.Substring(0, num).Trim(new char[7] { ' ', '-', '–', ',', ':', ';', '.' }), "\\s+", " ");
		if (text3.Length < 3 || text3.Count(char.IsLetter) < 3)
		{
			return false;
		}
		item = new PrescribedItemDto
		{
			DrugName = text3,
			Dosage = (match2.Success ? Regex.Replace(match2.Value.Trim(), "\\s+", " ") : string.Empty),
			Duration = (match3.Success ? (match3.Groups["n"].Value + " " + NormalizeUnit(match3.Groups["unit"].Value, match3.Groups["n"].Value)) : string.Empty)
		};
		return true;
	}

	public static bool TryParseMedicineLine(string line, out PrescribedItemDto item)
	{
		item = new PrescribedItemDto();
		if (string.IsNullOrWhiteSpace(line))
		{
			return false;
		}
		Match match = FormPrefixRegex().Match(line);
		string text;
		if (match.Success)
		{
			text = match.Groups["rest"].Value;
		}
		else
		{
			if (!StrengthRegex().IsMatch(line) || (!DosageRegex().IsMatch(line) && !DurationRegex().IsMatch(line)) || HeaderLineRegex().IsMatch(line))
			{
				return false;
			}
			text = line;
		}
		Match match2 = DosageRegex().Match(text);
		Match match3 = DurationRegex().Match(text);
		int num = text.Length;
		if (match2.Success)
		{
			num = Math.Min(num, match2.Index);
		}
		if (match3.Success)
		{
			num = Math.Min(num, match3.Index);
		}
		string text2 = text.Substring(0, num).Trim(new char[7] { ' ', '-', '–', ',', ':', ';', '.' });
		if (text2.Length < 2 || !text2.Any(char.IsLetter))
		{
			return false;
		}
		item = new PrescribedItemDto
		{
			DrugName = Regex.Replace(text2, "\\s+", " "),
			Dosage = (match2.Success ? Regex.Replace(match2.Value.Trim(), "\\s+", " ") : string.Empty),
			Duration = (match3.Success ? (match3.Groups["n"].Value + " " + NormalizeUnit(match3.Groups["unit"].Value, match3.Groups["n"].Value)) : string.Empty)
		};
		return true;
	}

	public static decimal? EstimateQuantity(string? dosage, string? duration)
	{
		decimal? num = DosesPerDay(dosage);
		decimal? num2 = DurationDays(duration);
		if (!num.HasValue || !num2.HasValue)
		{
			return null;
		}
		return Math.Ceiling(num.Value * num2.Value);
	}

	private static decimal? DosesPerDay(string? dosage)
	{
		if (string.IsNullOrWhiteSpace(dosage))
		{
			return null;
		}
		Match match = DosageRegex().Match(dosage);
		if (!match.Success)
		{
			return null;
		}
		if (match.Groups["tri"].Success)
		{
			return match.Groups["tri"].Value.Split(new char[2] { '-', '–' }, StringSplitOptions.RemoveEmptyEntries).Sum((string part) => (!decimal.TryParse(part.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var result)) ? 0m : result);
		}
		string text = Regex.Replace(match.Groups["abbr"].Value.ToLowerInvariant(), "\\s+", " ");
		switch (text)
		{
		case "od":
		case "qd":
		case "hs":
		case "once daily":
			return 1m;
		case "bd":
		case "bid":
		case "twice daily":
			return 2m;
		case "tds":
		case "tid":
		case "thrice daily":
			return 3m;
		case "qid":
			return 4m;
		default:
			if (text.Length > 0 && char.IsDigit(text[0]))
			{
				return text[0] - 48;
			}
			return null;
		}
	}

	private static decimal? DurationDays(string? duration)
	{
		if (string.IsNullOrWhiteSpace(duration))
		{
			return null;
		}
		Match match = DurationRegex().Match(duration);
		if (!match.Success || !int.TryParse(match.Groups["n"].Value, out var result))
		{
			return null;
		}
		return char.ToLowerInvariant(match.Groups["unit"].Value[0]) switch
		{
			'w' => (decimal)result * 7m, 
			'm' => (decimal)result * 30m, 
			_ => result, 
		};
	}

	private static string NormalizeUnit(string unit, string amount)
	{
		bool flag = amount == "1";
		return char.ToLowerInvariant(unit[0]) switch
		{
			'w' => flag ? "week" : "weeks", 
			'm' => flag ? "month" : "months", 
			_ => flag ? "day" : "days", 
		};
	}

	private static string FindDoctor(string text)
	{
		Match match = DoctorRegex().Match(text);
		string text2 = (match.Success ? CutAtStopWord(match.Groups["n"].Value) : string.Empty);
		if (text2.Length != 0)
		{
			return "Dr. " + text2;
		}
		return string.Empty;
	}

	private static string FindRegistration(string text)
	{
		Match match = RegistrationRegex().Match(text);
		if (!match.Success)
		{
			return string.Empty;
		}
		return match.Groups["r"].Value.Trim().ToUpperInvariant();
	}

	private static string FindPatient(string text)
	{
		foreach (Match item in PatientRegex().Matches(text))
		{
			string text2 = CutAtStopWord(item.Groups["n"].Value);
			if (text2.Length > 0 && !NameStopWords.Contains(text2.Split(' ')[0].Trim(new char[3] { '.', ',', ':' })))
			{
				return text2;
			}
		}
		return string.Empty;
	}

	private static string CutAtStopWord(string value)
	{
		string[] array = value.Split(new char[2] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> list = new List<string>();
		string[] array2 = array;
		foreach (string text in array2)
		{
			if (list.Count > 0 && NameStopWords.Contains(text.Trim(new char[3] { '.', ',', ':' })))
			{
				break;
			}
			list.Add(text);
		}
		return string.Join(' ', list).Trim(new char[3] { ' ', '.', ',' });
	}

	private static DateTime? FindDate(string text)
	{
		Match match = LabelledDateRegex().Match(text);
		string text2 = (match.Success ? match.Groups["d"].Value : AnyDateRegex().Match(text).Value);
		if (string.IsNullOrWhiteSpace(text2))
		{
			return null;
		}
		if (!TryParseDate(text2, out var date))
		{
			return null;
		}
		return date;
	}

	public static bool TryParseDate(string token, out DateTime date)
	{
		return DateTime.TryParseExact(Regex.Replace(token.Replace(',', ' ').Trim(), "\\s+", " "), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);
	}
}
