using Microsoft.EntityFrameworkCore;

namespace PharmaBill.Data.Persistence;

public sealed class DbInitializer(PharmaBillDbContext context)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        context.Database.MigrateAsync(cancellationToken);
}
