
namespace PharmaBill.Data.Persistence;

public interface IUnitOfWorkTransaction : IAsyncDisposable
{
	Task CommitAsync(CancellationToken cancellationToken = default(CancellationToken));

	Task RollbackAsync(CancellationToken cancellationToken = default(CancellationToken));
}
