using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Core.Services.Auth;

public interface ISocialOAuthSettings
{
	Task<GoogleOAuthClientCredentials> ResolveGoogleAsync(CancellationToken cancellationToken = default(CancellationToken));
}
