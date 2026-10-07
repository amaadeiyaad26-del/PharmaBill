using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;

namespace PharmaBill.Data.Persistence;

internal sealed class UnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction, IAsyncDisposable
{
	public Task CommitAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return transaction.CommitAsync(cancellationToken);
	}

	public Task RollbackAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		return transaction.RollbackAsync(cancellationToken);
	}

	public ValueTask DisposeAsync()
	{
		return transaction.DisposeAsync();
	}
}
