using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Core.Security;

public interface IEntitlementService
{
	Task<EntitlementStatus> GetStatusAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<bool> IsReadOnlyAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<bool> CanPerformAsync(ProtectedOperation operation, CancellationToken cancellationToken = default(CancellationToken));
}
