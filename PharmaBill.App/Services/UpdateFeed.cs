using System.Globalization;
using System.Text.Json;

namespace PharmaBill.App.Services;

/// <summary>A release as published in https://pharmaremit.com/updates/latest.json, after every field was checked.</summary>
public sealed record PublishedRelease(
	string Version,
	DateOnly Released,
	string DownloadUrl,
	long SizeBytes,
	string Sha256,
	string MinimumSupportedVersion,
	IReadOnlyList<string> Notes)
{
	public string SizeText => SizeBytes >= 1024 * 1024
		? (SizeBytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
		: Math.Max(1, SizeBytes / 1024).ToString(CultureInfo.InvariantCulture) + " KB";

	public string ReleasedText => Released.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}

/// <summary>
/// Reads the website's update file. Everything in it is untrusted: a file that is missing, is not JSON, lacks any field
/// or has a field of the wrong kind reads as "no update" (null), exactly as if there were none.
/// </summary>
public static class UpdateFeed
{
	public const string Url = "https://pharmaremit.com/updates/latest.json";

	/// <summary>The file is a few hundred bytes; anything far larger is not it.</summary>
	public const int MaxBytes = 64 * 1024;

	private const int MaxNotes = 30;

	private const int MaxNoteLength = 400;

	private const long MaxInstallerBytes = 4L * 1024 * 1024 * 1024;

	public static PublishedRelease? Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json) || json.Length > MaxBytes)
		{
			return null;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !TryString(root, "version", out string? version) || !TryParseVersion(version, out _)
				|| !TryString(root, "released", out string? releasedText) || !TryParseDate(releasedText!, out DateOnly released)
				|| !TryString(root, "downloadUrl", out string? downloadUrl) || !IsSafeDownloadUrl(downloadUrl)
				|| !root.TryGetProperty("sizeBytes", out JsonElement size) || size.ValueKind != JsonValueKind.Number
				|| !size.TryGetInt64(out long sizeBytes) || sizeBytes <= 0 || sizeBytes > MaxInstallerBytes
				|| !TryString(root, "sha256", out string? sha256) || sha256!.Length != 64 || !sha256.All(Uri.IsHexDigit)
				|| !TryString(root, "minimumSupportedVersion", out string? minimum) || !TryParseVersion(minimum, out _)
				|| !root.TryGetProperty("notes", out JsonElement notesElement) || notesElement.ValueKind != JsonValueKind.Array)
			{
				return null;
			}
			var notes = new List<string>();
			foreach (JsonElement note in notesElement.EnumerateArray())
			{
				if (note.ValueKind != JsonValueKind.String)
				{
					return null;
				}
				string text = Clean(note.GetString());
				if (text.Length > 0 && notes.Count < MaxNotes)
				{
					notes.Add(text.Length > MaxNoteLength ? text[..MaxNoteLength] + "…" : text);
				}
			}
			return new PublishedRelease(version!.Trim(), released, downloadUrl!.Trim(), sizeBytes, sha256.ToLowerInvariant(), minimum!.Trim(), notes);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>Only an https link to a named host, with no user name or password in it.</summary>
	public static bool IsSafeDownloadUrl(string? value) =>
		Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri)
		&& uri.Scheme == Uri.UriSchemeHttps
		&& !string.IsNullOrEmpty(uri.Host)
		&& string.IsNullOrEmpty(uri.UserInfo);

	/// <summary>1 to 4 dot-separated whole numbers ("1.0.12" or "1.0.12.0"); nothing else (no "v", no letters, no signs).</summary>
	public static bool TryParseVersion(string? value, out int[] parts)
	{
		parts = [];
		string[] pieces = (value ?? "").Trim().Split('.');
		if (pieces.Length is < 1 or > 4)
		{
			return false;
		}
		var numbers = new int[4];
		for (int i = 0; i < pieces.Length; i++)
		{
			if (pieces[i].Length is 0 or > 9 || !pieces[i].All(char.IsAsciiDigit))
			{
				return false;
			}
			numbers[i] = int.Parse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture);
		}
		parts = numbers;
		return true;
	}

	/// <summary>
	/// Compares two versions number by number (so 1.0.10 is newer than 1.0.9, and 1.0.12 equals 1.0.12.0).
	/// Throws on a version that is not 1 to 4 whole numbers.
	/// </summary>
	public static int CompareVersions(string left, string right)
	{
		if (!TryParseVersion(left, out int[] a))
		{
			throw new FormatException($"'{left}' is not a version.");
		}
		if (!TryParseVersion(right, out int[] b))
		{
			throw new FormatException($"'{right}' is not a version.");
		}
		for (int i = 0; i < 4; i++)
		{
			int compared = a[i].CompareTo(b[i]);
			if (compared != 0)
			{
				return compared;
			}
		}
		return 0;
	}

	/// <summary>True only when both are versions and the candidate is newer.</summary>
	public static bool IsNewer(string? candidate, string? current) =>
		TryParseVersion(candidate, out _) && TryParseVersion(current, out _) && CompareVersions(candidate!, current!) > 0;

	private static bool TryString(JsonElement root, string name, out string? value)
	{
		value = null;
		if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
		{
			return false;
		}
		value = element.GetString();
		return !string.IsNullOrWhiteSpace(value);
	}

	private static bool TryParseDate(string text, out DateOnly date)
	{
		text = text.Trim();
		if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
		{
			return true;
		}
		if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset moment))
		{
			date = DateOnly.FromDateTime(moment.UtcDateTime);
			return true;
		}
		return false;
	}

	// Notes are shown as plain text: control characters (other than ordinary spaces) are dropped.
	private static string Clean(string? text) =>
		new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
}
