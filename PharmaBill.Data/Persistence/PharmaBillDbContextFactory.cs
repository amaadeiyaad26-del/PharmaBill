using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PharmaBill.Data.Persistence;

public sealed class PharmaBillDbContextFactory : IDesignTimeDbContextFactory<PharmaBillDbContext>
{
    public PharmaBillDbContext CreateDbContext(string[] args)
    {
        SQLitePCL.Batteries_V2.Init();
        var options = new DbContextOptionsBuilder<PharmaBillDbContext>()
            .UseSqlite("Data Source=pharmabill-design.db")
            .Options;
        return new PharmaBillDbContext(options, new DatabaseDeviceId(Guid.Empty));
    }
}
