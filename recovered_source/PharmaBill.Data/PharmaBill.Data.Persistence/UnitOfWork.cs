using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Data.Persistence;

public sealed class UnitOfWork(PharmaBillDbContext context) : IUnitOfWork
{
	public PharmaBillDbContext Context => context;

	public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return new UnitOfWorkTransaction(await context.Database.BeginTransactionAsync(cancellationToken));
	}

	public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return context.SaveChangesAsync(cancellationToken);
	}
}
