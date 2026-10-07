using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Sync;

public sealed class CloudOAuthClientStore
{
	private sealed record OAuthOverrides(string? GoogleClientId, string? GoogleClientSecret)
	{
		public static OAuthOverrides Empty { get; } = new OAuthOverrides(null, null);
	}

	public const string GoogleFileName = "google-oauth-client.json";

	public const string CloudCredentialsFileName = "cloud_credentials.json";

	private readonly DatabaseStorageOptions _storage;

	private readonly string _overridePath;

	private readonly string _credentialsPath;

	private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

	public string GoogleConfigPath => Path.Combine(_storage.RootDirectory, "google-oauth-client.json");

	public string CloudCredentialsPath => _credentialsPath;

	public CloudOAuthClientStore(DatabaseStorageOptions storage)
	{
		_storage = storage;
		_overridePath = Path.Combine(storage.RootDirectory, "cloud-oauth-clients.dat");
		_credentialsPath = Path.Combine(storage.RootDirectory, "cloud_credentials.json");
	}

	public async Task<bool> HasCustomGoogleCredentialsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			GoogleOAuthClientConfig googleOAuthClientConfig = await ResolveGoogleAsync(requireConfigured: false, cancellationToken);
			return IsUsableGoogle(googleOAuthClientConfig.ClientId, googleOAuthClientConfig.ClientSecret);
		}
		catch
		{
			return false;
		}
	}

	public async Task<GoogleOAuthClientConfig?> LoadDisplayAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			return await ResolveGoogleAsync(requireConfigured: false, cancellationToken);
		}
		catch
		{
			return null;
		}
	}

	public async Task<GoogleOAuthClientConfig> ResolveGoogleAsync(bool requireConfigured = true, CancellationToken cancellationToken = default(CancellationToken))
	{
		await EnsureSeededAsync(cancellationToken);
		OAuthOverrides oAuthOverrides = await LoadOverridesAsync(cancellationToken);
		if (IsUsableGoogle(oAuthOverrides.GoogleClientId, oAuthOverrides.GoogleClientSecret))
		{
			return new GoogleOAuthClientConfig(oAuthOverrides.GoogleClientId, oAuthOverrides.GoogleClientSecret);
		}
		foreach (string item in CandidatePaths("google-oauth-client.json"))
		{
			if (File.Exists(item))
			{
				GoogleOAuthClientConfig googleOAuthClientConfig = TryReadGoogleFile(item);
				if ((object)googleOAuthClientConfig != null && IsUsableGoogle(googleOAuthClientConfig.ClientId, googleOAuthClientConfig.ClientSecret))
				{
					return googleOAuthClientConfig;
				}
			}
		}
		string environmentVariable = Environment.GetEnvironmentVariable("PHARMABILL_GOOGLE_CLIENT_ID");
		string environmentVariable2 = Environment.GetEnvironmentVariable("PHARMABILL_GOOGLE_CLIENT_SECRET");
		if (IsUsableGoogle(environmentVariable, environmentVariable2))
		{
			return new GoogleOAuthClientConfig(environmentVariable.Trim(), environmentVariable2.Trim());
		}
		GoogleOAuthClientConfig googleOAuthClientConfig2 = TryReadGoogleJson(ReadEmbedded("google-oauth-client.json"));
		if ((object)googleOAuthClientConfig2 != null && IsUsableGoogle(googleOAuthClientConfig2.ClientId, googleOAuthClientConfig2.ClientSecret))
		{
			return googleOAuthClientConfig2;
		}
		if (!requireConfigured)
		{
			return googleOAuthClientConfig2 ?? new GoogleOAuthClientConfig(string.Empty, string.Empty);
		}
		throw new CloudOAuthNotConfiguredException("Google Drive", "Google Drive is not linked to an OAuth client yet. Use ⚙ Configure Keys to paste your Google Desktop client ID and secret (from Google Cloud Console), then try Link Google Account again.");
	}

	public async Task SaveAdminOverridesAsync(string? googleClientId, string? googleClientSecret, CancellationToken cancellationToken = default(CancellationToken))
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (!OperatingSystem.IsWindows())
			{
				throw new PlatformNotSupportedException("OAuth keys are protected with Windows DPAPI.");
			}
			OAuthOverrides oAuthOverrides = await LoadOverridesUnlockedAsync(cancellationToken);
			OAuthOverrides updated = oAuthOverrides with
			{
				GoogleClientId = (NullIfPlaceholder(googleClientId) ?? oAuthOverrides.GoogleClientId),
				GoogleClientSecret = (string.IsNullOrWhiteSpace(googleClientSecret) ? oAuthOverrides.GoogleClientSecret : googleClientSecret.Trim())
			};
			Directory.CreateDirectory(_storage.RootDirectory);
			byte[] userData = JsonSerializer.SerializeToUtf8Bytes(updated);
			byte[] bytes = ProtectedData.Protect(userData, null, DataProtectionScope.CurrentUser);
			string credentialsTemp = $"{_credentialsPath}.{Guid.NewGuid():N}.tmp";
			await File.WriteAllBytesAsync(credentialsTemp, bytes, cancellationToken);
			File.Move(credentialsTemp, _credentialsPath, overwrite: true);
			string temporary = $"{_overridePath}.{Guid.NewGuid():N}.tmp";
			await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
			File.Move(temporary, _overridePath, overwrite: true);
			if (IsUsableGoogle(updated.GoogleClientId, updated.GoogleClientSecret))
			{
				string contents = $"{{\n  \"installed\": {{\n    \"client_id\": \"{Escape(updated.GoogleClientId)}\",\n    \"client_secret\": \"{Escape(updated.GoogleClientSecret)}\",\n    \"redirect_uris\": [ \"http://localhost\" ]\n  }}\n}}\n";
				await File.WriteAllTextAsync(GoogleConfigPath, contents, Encoding.UTF8, cancellationToken);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public static string ToUserFriendlyMessage(Exception exception)
	{
		for (Exception ex = exception; ex != null; ex = ex.InnerException)
		{
			if (ex is CloudOAuthNotConfiguredException ex2)
			{
				return ex2.Message;
			}
			string text = ex.Message ?? string.Empty;
			if (text.Contains("access_denied", StringComparison.OrdinalIgnoreCase) || text.Contains("authentication_canceled", StringComparison.OrdinalIgnoreCase) || text.Contains("user_cancelled", StringComparison.OrdinalIgnoreCase))
			{
				return "Sign-in was cancelled. Click Link again when you are ready.";
			}
			if (text.Contains("invalid_client", StringComparison.OrdinalIgnoreCase) || text.Contains("unauthorized_client", StringComparison.OrdinalIgnoreCase))
			{
				return "The OAuth client ID or secret is invalid. Open ⚙ Configure Keys and paste the credentials from Google Cloud Console.";
			}
			if (text.Contains("refresh token", StringComparison.OrdinalIgnoreCase))
			{
				return "Google did not return a refresh token. Remove PharmaBill from myaccount.google.com/permissions, then Link Google Account again.";
			}
		}
		string message = exception.Message;
		if (message.Contains("%LocalAppData%", StringComparison.OrdinalIgnoreCase) || message.Contains("google-oauth-client", StringComparison.OrdinalIgnoreCase) || message.Contains("PHARMABILL_", StringComparison.OrdinalIgnoreCase))
		{
			return "Cloud account linking is not configured yet. Use ⚙ Configure Keys to paste your Google client ID and secret, then try again.";
		}
		if (!string.IsNullOrWhiteSpace(message))
		{
			return message;
		}
		return "Cloud sign-in failed. Please try again.";
	}

	private async Task EnsureSeededAsync(CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(_storage.RootDirectory);
		if (!File.Exists(GoogleConfigPath))
		{
			string text = ReadEmbedded("google-oauth-client.json");
			if (!string.IsNullOrWhiteSpace(text))
			{
				await File.WriteAllTextAsync(GoogleConfigPath, text, Encoding.UTF8, cancellationToken);
			}
		}
	}

	private IEnumerable<string> CandidatePaths(string fileName)
	{
		yield return Path.Combine(_storage.RootDirectory, fileName);
		yield return Path.Combine(AppContext.BaseDirectory, fileName);
		yield return Path.Combine(AppContext.BaseDirectory, "docs", fileName);
		yield return Path.Combine(AppContext.BaseDirectory, "docs", fileName.Replace(".json", ".example.json", StringComparison.OrdinalIgnoreCase));
		DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
		int i = 0;
		while (i < 5 && current != null)
		{
			yield return Path.Combine(current.FullName, fileName);
			yield return Path.Combine(current.FullName, "docs", fileName.Replace(".json", ".example.json", StringComparison.OrdinalIgnoreCase));
			i++;
			current = current.Parent;
		}
	}

	private async Task<OAuthOverrides> LoadOverridesAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			return await LoadOverridesUnlockedAsync(cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<OAuthOverrides> LoadOverridesUnlockedAsync(CancellationToken cancellationToken)
	{
		if (!OperatingSystem.IsWindows())
		{
			return OAuthOverrides.Empty;
		}
		string[] array = new string[2] { _credentialsPath, _overridePath };
		foreach (string path in array)
		{
			if (!File.Exists(path))
			{
				continue;
			}
			try
			{
				OAuthOverrides oAuthOverrides = JsonSerializer.Deserialize<OAuthOverrides>(ProtectedData.Unprotect(await File.ReadAllBytesAsync(path, cancellationToken), null, DataProtectionScope.CurrentUser));
				if ((object)oAuthOverrides != null)
				{
					return oAuthOverrides;
				}
			}
			catch (Exception ex) when ((ex is CryptographicException || ex is JsonException) ? true : false)
			{
			}
		}
		return OAuthOverrides.Empty;
	}

	private static GoogleOAuthClientConfig? TryReadGoogleFile(string path)
	{
		try
		{
			return TryReadGoogleJson(File.ReadAllText(path));
		}
		catch
		{
			return null;
		}
	}

	private static GoogleOAuthClientConfig? TryReadGoogleJson(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(json);
		JsonElement rootElement = jsonDocument.RootElement;
		if (rootElement.TryGetProperty("installed", out var value))
		{
			string text = (value.TryGetProperty("client_id", out var value2) ? value2.GetString() : null);
			string text2 = (value.TryGetProperty("client_secret", out var value3) ? value3.GetString() : null);
			if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(text2))
			{
				return new GoogleOAuthClientConfig(text, text2);
			}
		}
		string text3 = (rootElement.TryGetProperty("client_id", out var value4) ? value4.GetString() : null);
		string text4 = (rootElement.TryGetProperty("client_secret", out var value5) ? value5.GetString() : null);
		return (!string.IsNullOrWhiteSpace(text3) && !string.IsNullOrWhiteSpace(text4)) ? new GoogleOAuthClientConfig(text3, text4) : null;
	}

	private static string? ReadEmbedded(string fileName)
	{
		Assembly assembly = typeof(CloudOAuthClientStore).Assembly;
		string text = assembly.GetManifestResourceNames().FirstOrDefault((string name) => name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
		if (text == null)
		{
			return null;
		}
		using Stream stream = assembly.GetManifestResourceStream(text);
		if (stream == null)
		{
			return null;
		}
		using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8);
		return streamReader.ReadToEnd();
	}

	private static bool IsUsableGoogle(string? clientId, string? clientSecret)
	{
		if (!string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(clientSecret) && !IsPlaceholder(clientId))
		{
			return !IsPlaceholder(clientSecret);
		}
		return false;
	}

	private static bool IsPlaceholder(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}
		string text = value.Trim();
		if (!text.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) && !text.Contains("YOUR-", StringComparison.OrdinalIgnoreCase))
		{
			return text.Equals("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string? NullIfPlaceholder(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value) && !IsPlaceholder(value))
		{
			return value.Trim();
		}
		return null;
	}

	private static string Escape(string value)
	{
		return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
	}
}
