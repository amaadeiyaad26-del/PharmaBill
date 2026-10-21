using System;
using System.Linq;
using PharmaBill.Core.Entities;

namespace PharmaBill.Data.Services;

/// <summary>
/// How the shop presents itself on its own screens. Empty parts are null and are simply left out; there is no
/// "Not provided" placeholder.
/// </summary>
public sealed record ShopIdentity(string Name, string? Address, string? Phone, string? LogoPath)
{
	/// <summary>Address and phone on one line, separated by a middle dot; empty when both are missing.</summary>
	public string ContactLine => string.Join("  ·  ", new[] { Address, Phone }.Where(part => !string.IsNullOrWhiteSpace(part)));
}

public static class ShopIdentityResolver
{
	public const string FallbackName = "Your pharmacy";

	/// <summary>
	/// The name always comes from Firm Profile. Address and phone come from the active branch when it has its own,
	/// otherwise from Firm Profile.
	/// </summary>
	public static ShopIdentity Resolve(PharmacyProfile? profile, Branch? activeBranch, string? logoPath)
	{
		string name = Clean(profile?.Name) ?? FallbackName;
		string? address = OneLine(activeBranch?.Address) ?? OneLine(profile?.Address);
		string? phone = Clean(activeBranch?.ContactPhone) ?? Clean(profile?.Phone);
		return new ShopIdentity(name, address, phone, Clean(logoPath));
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
