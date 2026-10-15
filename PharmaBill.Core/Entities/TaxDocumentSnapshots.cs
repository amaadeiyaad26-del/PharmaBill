using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PharmaBill.Core.Entities;

/// <summary>A licence as printed on a tax document.</summary>
public sealed record LicenceSnapshot(string Type, string Number, DateOnly? ExpiresOn = null);

/// <summary>
/// Seller (pharmacy) details exactly as they stood when a sale or wholesale invoice was saved.
/// Printed documents and returns read this back; they never re-read the pharmacy profile.
/// </summary>
public sealed record SellerSnapshot
{
	public string Name { get; init; } = string.Empty;
	public string? LegalName { get; init; }
	public string? Address { get; init; }
	public string? Phone { get; init; }
	public string? Gstin { get; init; }
	public string? BranchGstin { get; init; }
	public string? BranchDrugLicenceNo { get; init; }
	public string? StateCode { get; init; }
	public string? StateName { get; init; }
	public IReadOnlyList<LicenceSnapshot> Licences { get; init; } = Array.Empty<LicenceSnapshot>();
	public string? CompetentPersonName { get; init; }
	public string? CompetentPersonQualification { get; init; }
	public string? CompetentPersonRegistrationNumber { get; init; }
	public string? BankName { get; init; }
	public string? BankAccountName { get; init; }
	public string? BankIfsc { get; init; }

	/// <summary>Retail print branding captured at save (extra licence lines, FSSAI, address line 2, terms).</summary>
	public IReadOnlyList<string> ExtraLicenceLines { get; init; } = Array.Empty<string>();
	public string? FssaiNumber { get; init; }
	public string? AddressLine2 { get; init; }
	public string? TermsText { get; init; }
}

/// <summary>Buyer (wholesale customer or retail patient) details exactly as they stood at save.</summary>
public sealed record BuyerSnapshot
{
	public string Name { get; init; } = string.Empty;
	public string? Address { get; init; }
	public string? Phone { get; init; }
	public string? Gstin { get; init; }
	public string? StateCode { get; init; }
	public string? StateName { get; init; }
	public IReadOnlyList<LicenceSnapshot> Licences { get; init; } = Array.Empty<LicenceSnapshot>();
	public string? PrescriberName { get; init; }
	public string? PrescriberRegistrationNumber { get; init; }
	public string? PrescriptionReference { get; init; }
}

public static class TaxDocumentSnapshot
{
	private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.General);

	public static string Serialize<T>(T snapshot) => JsonSerializer.Serialize(snapshot, Options);

	public static T? Deserialize<T>(string? json) where T : class =>
		string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json, Options);
}
