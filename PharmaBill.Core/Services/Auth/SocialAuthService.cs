using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;

namespace PharmaBill.Core.Services.Auth;

public sealed class SocialAuthService(ISocialOAuthSettings settings)
{
	private static readonly string[] GoogleScopes = new string[3] { "openid", "email", "profile" };

	public const string OfflineMessage = "Internet connection required for Google sign-in. Please use your local PIN or password to sign in offline.";

	public async Task<SocialAuthProfile> SignInWithGoogleAsync(string tokenStoreDirectory, CancellationToken cancellationToken = default(CancellationToken))
	{
		EnsureOnline();
		ArgumentException.ThrowIfNullOrWhiteSpace(tokenStoreDirectory, "tokenStoreDirectory");
		GoogleOAuthClientCredentials googleOAuthClientCredentials = await settings.ResolveGoogleAsync(cancellationToken);
		if (string.IsNullOrWhiteSpace(googleOAuthClientCredentials.ClientId) || string.IsNullOrWhiteSpace(googleOAuthClientCredentials.ClientSecret))
		{
			throw new InvalidOperationException("Google Sign-In is not configured. Use Configure Keys or set PHARMABILL_GOOGLE_CLIENT_ID and PHARMABILL_GOOGLE_CLIENT_SECRET.");
		}
		Directory.CreateDirectory(tokenStoreDirectory);
		UserCredential userCredential = await GoogleWebAuthorizationBroker.AuthorizeAsync(new ClientSecrets
		{
			ClientId = googleOAuthClientCredentials.ClientId.Trim(),
			ClientSecret = googleOAuthClientCredentials.ClientSecret.Trim()
		}, GoogleScopes, "social-signin", cancellationToken, new FileDataStore(tokenStoreDirectory, fullPath: true));
		string email = await TryReadGoogleEmailAsync(userCredential, cancellationToken);
		string text = await TryReadGoogleNameAsync(userCredential, cancellationToken);
		string text2 = TryReadGoogleSubject(userCredential) ?? email ?? userCredential.UserId;
		if (string.IsNullOrWhiteSpace(text2))
		{
			throw new InvalidOperationException("Google did not return a user identifier.");
		}
		return new SocialAuthProfile("Google", text2.Trim(), string.IsNullOrWhiteSpace(email) ? null : email.Trim(), string.IsNullOrWhiteSpace(text) ? null : text.Trim());
	}

	public static async Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!NetworkInterface.GetIsNetworkAvailable())
		{
			return false;
		}
		try
		{
			using HttpClient client = new HttpClient
			{
				Timeout = TimeSpan.FromSeconds(4L)
			};
			using HttpResponseMessage httpResponseMessage = await client.GetAsync("https://www.google.com/generate_204", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			return httpResponseMessage.IsSuccessStatusCode;
		}
		catch (Exception ex) when ((ex is HttpRequestException || ex is SocketException || ex is TaskCanceledException) ? true : false)
		{
			return false;
		}
	}

	private static void EnsureOnline()
	{
		if (!NetworkInterface.GetIsNetworkAvailable())
		{
			throw new InvalidOperationException("Internet connection required for Google sign-in. Please use your local PIN or password to sign in offline.");
		}
	}

	private static JsonElement ParseJwtPayload(string jwt)
	{
		string[] array = jwt.Split('.');
		if (array.Length < 2)
		{
			throw new InvalidOperationException("Google id_token is malformed.");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(array[1])));
		return jsonDocument.RootElement.Clone();
	}

	private static byte[] Base64UrlDecode(string input)
	{
		string text = input.Replace('-', '+').Replace('_', '/');
		switch (text.Length % 4)
		{
		case 2:
			text += "==";
			break;
		case 3:
			text += "=";
			break;
		}
		return Convert.FromBase64String(text);
	}

	private static string? TryReadGoogleSubject(UserCredential credential)
	{
		if (!string.IsNullOrWhiteSpace(credential.Token.IdToken))
		{
			try
			{
				if (ParseJwtPayload(credential.Token.IdToken).TryGetProperty("sub", out var value))
				{
					return value.GetString();
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private static async Task<string?> TryReadGoogleEmailAsync(UserCredential credential, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(credential.Token.IdToken))
		{
			try
			{
				if (ParseJwtPayload(credential.Token.IdToken).TryGetProperty("email", out var value))
				{
					return value.GetString();
				}
			}
			catch
			{
			}
		}
		try
		{
			using HttpClient http = new HttpClient();
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token.AccessToken);
			using JsonDocument jsonDocument = JsonDocument.Parse(await http.GetStringAsync("https://www.googleapis.com/oauth2/v3/userinfo", cancellationToken));
			if (jsonDocument.RootElement.TryGetProperty("email", out var value2))
			{
				return value2.GetString();
			}
		}
		catch
		{
		}
		return null;
	}

	private static async Task<string?> TryReadGoogleNameAsync(UserCredential credential, CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(credential.Token.IdToken))
		{
			try
			{
				if (ParseJwtPayload(credential.Token.IdToken).TryGetProperty("name", out var value))
				{
					return value.GetString();
				}
			}
			catch
			{
			}
		}
		try
		{
			using HttpClient http = new HttpClient();
			http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token.AccessToken);
			using JsonDocument jsonDocument = JsonDocument.Parse(await http.GetStringAsync("https://www.googleapis.com/oauth2/v3/userinfo", cancellationToken));
			if (jsonDocument.RootElement.TryGetProperty("name", out var value2))
			{
				return value2.GetString();
			}
		}
		catch
		{
		}
		return null;
	}
}
