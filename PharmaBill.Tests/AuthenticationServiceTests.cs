using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class AuthenticationServiceTests
{
    [Theory]
    [InlineData("dummy", "1234")]
    [InlineData("  dummy  ", "  1234  ")]
    [InlineData("DUMMY", "1234")]
    [InlineData("Dummy", "1234 ")]
    public async Task Authenticate_AcceptsAccountsCreatedWithTrimmedDetails(string username, string secret)
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.AppUsers.Add(new AppUser
        {
            UserName = "dummy",
            DisplayName = "Dummy",
            Role = PharmaBill.Core.Entities.UserRole.BillingClerk,
            PasswordHash = PasswordHasher.Hash("1234"),
            IsActive = true
        });
        await database.Context.SaveChangesAsync();
        var service = new AuthenticationService(database.Context);

        var user = await service.AuthenticateAsync(username, secret);

        Assert.NotNull(user);
    }

    [Theory]
    [InlineData("dummy", "9999")]
    [InlineData("other", "1234")]
    public async Task Authenticate_RejectsWrongDetails(string username, string secret)
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.AppUsers.Add(new AppUser
        {
            UserName = "dummy",
            DisplayName = "Dummy",
            Role = PharmaBill.Core.Entities.UserRole.BillingClerk,
            PasswordHash = PasswordHasher.Hash("1234"),
            IsActive = true
        });
        await database.Context.SaveChangesAsync();
        var service = new AuthenticationService(database.Context);

        Assert.Null(await service.AuthenticateAsync(username, secret));
    }
}
