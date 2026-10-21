using System;
using System.Collections.Generic;
using System.Linq;
using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Services;

/// <summary>
/// How the shop presents itself on its own screens. Empty parts are null and are simply left out; there is no
/// "Not provided" placeholder.
/// </summary>
public sealed record ShopIdentity(string Name, string? Address, string? Phone, byte[]? LogoPng)
{
	/// <summary>Address and phone on one line, separated by a middle dot; empty when both are missing.</summary>
	public string ContactLine => string.Join("  ·  ", new[] { Address, Phone }.Where(part => !string.IsNullOrWhiteSpace(part)));

	/// <summary>One or two letters for the generated monogram when no logo is uploaded.</summary>
	public string Initials => ShopIdentityResolver.Initials(Name);
}

public static class ShopIdentityResolver
{
	public const string FallbackName = "Your pharmacy";

	// Prefixes and legal suffixes that would make poor initials ("M/s Sameer Chemists Pvt Ltd" -> "SC").
	private static readonly HashSet<string> IgnoredWords = new(StringComparer.OrdinalIgnoreCase)
	{
		"m/s", "ms", "messrs", "the", "and", "&", "of", "pvt", "pvt.", "private", "ltd", "ltd.", "limited", "llp", "co", "co."
	};

	/// <summary>
	/// The name always comes from Firm Profile, and so does the logo. Address and phone come from the active branch
	/// when it has its own, otherwise from Firm Profile.
	/// </summary>
	public static ShopIdentity Resolve(PharmacyProfile? profile, Branch? activeBranch)
	{
		string name = Clean(profile?.Name) ?? FallbackName;
		string? address = OneLine(activeBranch?.Address) ?? OneLine(profile?.Address);
		string? phone = Clean(activeBranch?.ContactPhone) ?? Clean(profile?.Phone);
		byte[]? logo = profile?.LogoPng is { Length: > 0 } png ? png : null;
		return new ShopIdentity(name, address, phone, logo);
	}

	/// <summary>
	/// First letters of the first two significant words ("Sameer Chemists" -> "SC"); one letter for a single word
	/// ("Apollo" -> "A"). Prefixes such as "M/s" and suffixes such as "Pvt Ltd" are skipped.
	/// </summary>
	public static string Initials(string? name)
	{
		List<string> words = (name ?? string.Empty)
			.Split(new[] { ' ', '\t', '-', ',', '.' }, StringSplitOptions.RemoveEmptyEntries)
			.Where(word => !IgnoredWords.Contains(word) && word.Any(char.IsLetterOrDigit))
			.ToList();
		string initials = string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word.First(char.IsLetterOrDigit))));
		return initials.Length > 0 ? initials : "P";
	}

	private static string? Clean(string? value)
	{
		string trimmed = value?.Trim() ?? string.Empty;
		return trimmed.Length == 0 ? null : trimmed;
	}

	/// <summary>A multi-line address reads as one line: "12 Main Road, Srinagar".</summary>
	private static string? OneLine(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		string[] parts = value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(part => part.Trim().TrimEnd(','))
			.Where(part => part.Length > 0)
			.ToArray();
		return parts.Length == 0 ? null : string.Join(", ", parts);
	}
}
