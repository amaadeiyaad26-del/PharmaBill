using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Data.Services;

public interface ISubscriptionProvider
{
	Task<SubscriptionState> GetStateAsync(CancellationToken cancellationToken = default(CancellationToken));
}
