using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SQLitePCL;

namespace PharmaBill.Data.Persistence;

public sealed class PharmaBillDbContextFactory : IDesignTimeDbContextFactory<PharmaBillDbContext>
{
	public PharmaBillDbContext CreateDbContext(string[] args)
	{
		Batteries_V2.Init();
		return new PharmaBillDbContext(new DbContextOptionsBuilder<PharmaBillDbContext>().UseSqlite("Data Source=pharmabill-design.db").Options, new DatabaseDeviceId(Guid.Empty));
	}
}
