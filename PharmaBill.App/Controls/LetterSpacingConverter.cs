using System;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace PharmaBill.App.Controls;

/// <summary>
/// Light tracking for display headings (WPF text has no letter-spacing): a hair space between the letters of each
/// word, held to its neighbours by a word joiner so lines still break only between words. Use for display only
/// and give the element the plain text as its automation name.
/// </summary>
public sealed class LetterSpacingConverter : IValueConverter
{
	private const string Tracking = " ⁠";

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		string text = value as string ?? string.Empty;
		StringBuilder builder = new StringBuilder(text.Length * 3);
		for (int i = 0; i < text.Length; i++)
		{
			builder.Append(text[i]);
			bool inWord = !char.IsWhiteSpace(text[i]) && i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]);
			if (inWord && !char.IsHighSurrogate(text[i]))
			{
				builder.Append(Tracking);
			}
		}

		return builder.ToString();
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		(value as string ?? string.Empty).Replace(Tracking, string.Empty, StringComparison.Ordinal);
}
