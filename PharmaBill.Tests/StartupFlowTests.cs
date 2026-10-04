using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Core.Entities;
using PharmaBill.Core.Security;
using PharmaBill.Data.Services;
using PharmaBill.Data.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PharmaBill.Tests;

public sealed class StartupFlowTests
{
    [Fact]
    public async Task SuccessfulLoginShowsMainAndOnlyMainCloseShutsDown()
    {
        var shutdownCount = 0;
        var mainShown = false;
        Action? closeMain = null;

        await StartupFlow.RunAsync(
            setupRequired: false,
            showSetupAsync: () => Task.FromResult(true),
            isAuthenticated: () => false,
            showLoginAsync: () => Task.FromResult(true),
            showMain: onMainClosed =>
            {
                mainShown = true;
                closeMain = onMainClosed;
            },
            shutdown: () => shutdownCount++);

        Assert.True(mainShown);
        Assert.Equal(0, shutdownCount);
        Assert.NotNull(closeMain);

        closeMain();

        Assert.Equal(1, shutdownCount);
    }

    [Fact]
    public async Task CancellingLoginShutsDownWithoutCreatingMain()
    {
        var shutdownCount = 0;
        var mainShown = false;

        await StartupFlow.RunAsync(
            setupRequired: false,
            showSetupAsync: () => Task.FromResult(true),
            isAuthenticated: () => false,
            showLoginAsync: () => Task.FromResult(false),
            showMain: _ => mainShown = true,
            shutdown: () => shutdownCount++);

        Assert.False(mainShown);
        Assert.Equal(1, shutdownCount);
    }

    [Fact]
    public async Task OwnerCreatedDuringSetupSignsInAndOpensMainWithoutShowingLogin()
    {
        var session = new CurrentSession();
        var loginShown = false;
        var mainShown = false;
        var owner = new AppUser { UserName = "owner", Role = UserRole.Owner };

        await StartupFlow.RunAsync(
            setupRequired: true,
            showSetupAsync: () =>
            {
                session.SignIn(owner);
                return Task.FromResult(true);
            },
            isAuthenticated: () => session.IsAuthenticated,
            showLoginAsync: () =>
            {
                loginShown = true;
                return Task.FromResult(false);
            },
            showMain: _ => mainShown = true,
            shutdown: () => Assert.Fail("Successful setup must not shut down."));

        Assert.False(loginShown);
        Assert.True(mainShown);
    }

    [Fact]
    public async Task FailedLoginLeavesLoginOpenAndDoesNotPreventLaterSuccess()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var owner = new AppUser
        {
            UserName = "owner",
            DisplayName = "Owner",
            Role = UserRole.Owner,
            PasswordHash = PasswordHasher.Hash("correct-pin")
        };
        database.Context.AppUsers.Add(owner);
        await database.Context.SaveChangesAsync();

        var session = new CurrentSession();
        var viewModel = new AuthenticationViewModel(
            new AuthenticationService(database.Context),
            session)
        {
            Username = "owner",
            Secret = "wrong-pin"
        };
        var authenticatedEvents = 0;
        viewModel.Authenticated += (_, _) => authenticatedEvents++;

        await viewModel.AuthenticateCommand.ExecuteAsync(null);

        Assert.False(session.IsAuthenticated);
        Assert.Equal(0, authenticatedEvents);
        Assert.Equal("Wrong username or password (4 tries left)", viewModel.ErrorMessage);

        var shutdownCount = 0;
        var mainShown = false;
        await StartupFlow.RunAsync(
            setupRequired: false,
            showSetupAsync: () => Task.FromResult(true),
            isAuthenticated: () => session.IsAuthenticated,
            showLoginAsync: async () =>
            {
                Assert.False(session.IsAuthenticated);
                viewModel.Secret = "correct-pin";
                await viewModel.AuthenticateCommand.ExecuteAsync(null);
                return session.IsAuthenticated;
            },
            showMain: _ => mainShown = true,
            shutdown: () => shutdownCount++);

        Assert.True(mainShown);
        Assert.Equal(0, shutdownCount);
        Assert.Equal(1, authenticatedEvents);
    }

    [Fact]
    public async Task FifthWrongLoginTemporarilyLocksUnlockButton()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        database.Context.AppUsers.Add(new AppUser
        {
            UserName = "owner",
            DisplayName = "Owner",
            Role = UserRole.Owner,
            PasswordHash = PasswordHasher.Hash("correct-pin")
        });
        await database.Context.SaveChangesAsync();

        var viewModel = new AuthenticationViewModel(
            new AuthenticationService(database.Context),
            new CurrentSession())
        {
            Username = "owner",
            Secret = "wrong-pin"
        };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await viewModel.AuthenticateCommand.ExecuteAsync(null);
        }

        Assert.True(viewModel.IsLockedOut);
        Assert.False(viewModel.CanUnlock);
        Assert.Equal(30, viewModel.LockoutSecondsRemaining);
        Assert.Contains("30 seconds", viewModel.ErrorMessage);
        await viewModel.AuthenticateCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsLockedOut);
    }

    [Fact]
    public async Task RecoveryCodeResetsOwnerPasswordOnceAndWritesAuditLog()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var owner = new AppUser
        {
            UserName = "owner",
            DisplayName = "Owner",
            Role = UserRole.Owner,
            PasswordHash = PasswordHasher.Hash("old-pin")
        };
        database.Context.AppUsers.Add(owner);
        await database.Context.SaveChangesAsync();

        var dataPath = Path.Combine(Path.GetTempPath(), $"PharmaBill.Recovery.{Guid.NewGuid():N}");
        try
        {
            var storage = new DatabaseStorageOptions(dataPath);
            var store = new RecoveryCodeStore(storage);
            var code = await store.CreateAsync();
            var services = new ServiceCollection();
            services.AddSingleton(database.Context);
            await using var provider = services.BuildServiceProvider();
            var recovery = new OwnerRecoveryService(provider.GetRequiredService<IServiceScopeFactory>(), store);

            Assert.False(await recovery.ResetOwnerPasswordAsync("owner", "incorrect", "new-pin"));
            Assert.True(await recovery.ResetOwnerPasswordAsync("owner", code, "new-pin"));
            Assert.False(await recovery.ResetOwnerPasswordAsync("owner", code, "another-pin"));
            Assert.Null(await new AuthenticationService(database.Context).AuthenticateAsync("owner", "old-pin"));
            Assert.NotNull(await new AuthenticationService(database.Context).AuthenticateAsync("owner", "new-pin"));
            Assert.Contains(await database.Context.AuditLogs.ToListAsync(),
                entry => entry.Action == "OwnerPasswordResetUsingRecoveryCode");
        }
        finally
        {
            if (Directory.Exists(dataPath))
            {
                Directory.Delete(dataPath, recursive: true);
            }
        }
    }
}
