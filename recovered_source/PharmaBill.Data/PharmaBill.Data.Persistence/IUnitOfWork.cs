using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Data.Persistence;

public interface IUnitOfWork
{
	PharmaBillDbContext Context { get; }

	Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task<int> SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken));
}
