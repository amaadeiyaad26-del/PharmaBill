using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Data.Services;

public sealed class StubSubscriptionProvider : ISubscriptionProvider
{
	public Task<SubscriptionState> GetStateAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.FromResult(new SubscriptionState(IsSubscribed: true, IsOfflineGrace: false));
	}
}
