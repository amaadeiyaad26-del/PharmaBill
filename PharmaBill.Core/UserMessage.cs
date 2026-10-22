using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace PharmaBill.Core;

/// <summary>
/// Turns an exception into a sentence for a shopkeeper's screen. Parameter names, framework wording and database
/// internals are developer text: they stay in the log, never on the screen. Messages the app writes for people
/// ("Quantity exceeds available stock…") pass through unchanged.
/// </summary>
public static class UserMessage
{
	private static readonly Regex ParameterSuffix = new Regex(@"\s*\(Parameter '[^']*'\)", RegexOptions.Compiled);

	private static readonly string[] FrameworkArgumentWording =
	[
		"Value cannot be null",
		"The value cannot be an empty string",
		"The value cannot be null or empty",
		"Specified argument was out of the range",
		"Value does not fall within the expected range",
		"The input string",
		"Input string was not in a correct format"
	];

	public const string Fallback = "Something went wrong and nothing was saved. Try again; if it keeps happening, restart Pharma Remit.";

	public static string Of(Exception exception)
	{
		Exception actual = Unwrap(exception);
		string message = ParameterSuffix.Replace(actual.Message ?? string.Empty, string.Empty).Trim();
		string typeName = actual.GetType().FullName ?? string.Empty;

		if (actual is ArgumentException argument && FrameworkArgumentWording.Any(prefix => message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
		{
			return string.IsNullOrWhiteSpace(argument.ParamName) ? "Something required is missing or not valid. Check the entries and try again." : FieldName(argument.ParamName) + " is required.";
		}
		if (typeName.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal) || typeName.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
		{
			return "The shop's records could not be updated just now, so nothing was changed. Try again; if it keeps happening, restart Pharma Remit.";
		}
		if (actual is NullReferenceException or IndexOutOfRangeException or InvalidCastException || string.IsNullOrWhiteSpace(message))
		{
			return Fallback;
		}
		return message;
	}

	private static Exception Unwrap(Exception exception)
	{
		Exception current = exception;
		while (true)
		{
			if (current is AggregateException { InnerExceptions.Count: 1 } aggregate)
			{
				current = aggregate.InnerExceptions[0];
			}
			else if (current is TargetInvocationException { InnerException: not null } invocation)
			{
				current = invocation.InnerException;
			}
			else
			{
				return current;
			}
		}
	}

	/// <summary>"input.PrescriberRegistrationNumber" → "Prescriber registration number".</summary>
	public static string FieldName(string parameterName)
	{
		string last = parameterName.Split('.').Last().Trim();
		var words = new StringBuilder();
		for (int i = 0; i < last.Length; i++)
		{
			char c = last[i];
			if (i > 0 && char.IsUpper(c) && !char.IsUpper(last[i - 1]))
			{
				words.Append(' ');
			}
			words.Append(words.Length == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
		}
		return words.Length == 0 ? "A required entry" : words.ToString();
	}
}
