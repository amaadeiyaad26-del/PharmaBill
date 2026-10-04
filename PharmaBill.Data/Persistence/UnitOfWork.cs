using Microsoft.EntityFrameworkCore.Storage;

namespace PharmaBill.Data.Persistence;

public sealed class UnitOfWork(PharmaBillDbContext context) : IUnitOfWork
{
    public PharmaBillDbContext Context => context;

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
        new UnitOfWorkTransaction(await context.Database.BeginTransactionAsync(cancellationToken));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

internal sealed class UnitOfWorkTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    : IUnitOfWorkTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        transaction.RollbackAsync(cancellationToken);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
