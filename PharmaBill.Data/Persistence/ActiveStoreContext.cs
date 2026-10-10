using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PharmaBill.Data.Persistence;

/// <summary>
/// Tracks which pharmacy store database is active under %LocalAppData%\PharmaBill\{StoreSlug}\.
/// Legacy installs without a pointer keep using the shared <c>data</c> folder.
/// </summary>
public sealed class ActiveStoreContext
{
	public const string LegacyStoreSlug = "data";
	private const string PointerFileName = "active-store.txt";

	private readonly string _rootDirectory;
	private string _storeSlug;

	public ActiveStoreContext(string rootDirectory)
	{
		_rootDirectory = Path.GetFullPath(rootDirectory);
		Directory.CreateDirectory(_rootDirectory);
		_storeSlug = ReadPointer() ?? LegacyStoreSlug;
		Directory.CreateDirectory(StoreDirectory);
	}

	public string RootDirectory => _rootDirectory;

	public string StoreSlug => _storeSlug;

	public string StoreDirectory => Path.Combine(_rootDirectory, _storeSlug);

	public string DatabasePath => Path.Combine(StoreDirectory, AppDataPaths.DatabaseFileName);

	public string PointerPath => Path.Combine(_rootDirectory, PointerFileName);

	public void SetActiveStore(string storeSlug)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(storeSlug);
		string slug = SanitizeStoreName(storeSlug);
		if (string.IsNullOrWhiteSpace(slug))
		{
			slug = LegacyStoreSlug;
		}

		_storeSlug = slug;
		Directory.CreateDirectory(StoreDirectory);
		File.WriteAllText(PointerPath, slug, Encoding.UTF8);
	}

	/// <summary>
	/// "SAMEER CHEMISTS" → "SameerChemists". Falls back to "Store" when empty after sanitize.
	/// </summary>
	public static string SanitizeStoreName(string? pharmacyName)
	{
		if (string.IsNullOrWhiteSpace(pharmacyName))
		{
			return "Store";
		}

		string[] words = Regex.Split(pharmacyName.Trim(), @"[\s_\-]+")
			.Where(word => !string.IsNullOrWhiteSpace(word))
			.ToArray();
		if (words.Length == 0)
		{
			return "Store";
		}

		StringBuilder builder = new StringBuilder();
		foreach (string word in words)
		{
			string cleaned = Regex.Replace(word, @"[^A-Za-z0-9]", string.Empty);
			if (cleaned.Length == 0)
			{
				continue;
			}

			builder.Append(char.ToUpperInvariant(cleaned[0]));
			if (cleaned.Length > 1)
			{
				builder.Append(cleaned[1..].ToLowerInvariant());
			}
		}

		string result = builder.ToString();
		if (string.IsNullOrWhiteSpace(result))
		{
			return "Store";
		}

		if (result.Length > 64)
		{
			result = result[..64];
		}

		return result;
	}

	private string? ReadPointer()
	{
		try
		{
			if (!File.Exists(PointerPath))
			{
				return null;
			}

			string value = File.ReadAllText(PointerPath).Trim();
			return string.IsNullOrWhiteSpace(value) ? null : SanitizeStoreName(value);
		}
		catch
		{
			return null;
		}
	}
}
