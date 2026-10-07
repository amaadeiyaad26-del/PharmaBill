using System;
using System.Text.Json.Serialization;

namespace PharmaBill.App.Services;

public sealed class ReleaseManifest
{
	[JsonPropertyName("latest_version")]
	public string LatestVersion { get; set; } = "0.0.0";

	[JsonPropertyName("min_supported_version")]
	public string? MinSupportedVersion { get; set; }

	[JsonPropertyName("is_critical")]
	public bool IsCritical { get; set; }

	[JsonPropertyName("download_url")]
	public string? DownloadUrl { get; set; }

	[JsonPropertyName("release_notes_url")]
	public string? ReleaseNotesUrl { get; set; }

	[JsonPropertyName("microsoft_store_product_id")]
	public string? MicrosoftStoreProductId { get; set; }

	[JsonPropertyName("published_at_utc")]
	public DateTimeOffset? PublishedAtUtc { get; set; }

	[JsonPropertyName("message")]
	public string? Message { get; set; }
}
