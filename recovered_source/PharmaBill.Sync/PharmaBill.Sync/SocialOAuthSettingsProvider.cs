using System;
using System.Threading;
using System.Threading.Tasks;
using PharmaBill.Core.Services.Auth;

namespace PharmaBill.Sync;

public sealed class SocialOAuthSettingsProvider(CloudOAuthClientStore oauthClients) : ISocialOAuthSettings
{
	public async Task<GoogleOAuthClientCredentials> ResolveGoogleAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			GoogleOAuthClientConfig googleOAuthClientConfig = await oauthClients.ResolveGoogleAsync(requireConfigured: false, cancellationToken);
			return new GoogleOAuthClientCredentials(googleOAuthClientConfig.ClientId, googleOAuthClientConfig.ClientSecret);
		}
		catch
		{
			string? clientId = Environment.GetEnvironmentVariable("PHARMABILL_GOOGLE_CLIENT_ID") ?? string.Empty;
			string clientSecret = Environment.GetEnvironmentVariable("PHARMABILL_GOOGLE_CLIENT_SECRET") ?? string.Empty;
			return new GoogleOAuthClientCredentials(clientId, clientSecret);
		}
	}
}
