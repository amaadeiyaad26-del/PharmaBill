namespace PharmaBill.App.Services;

public static class StartupFlow
{
    public static async Task RunAsync(
        bool setupRequired,
        Func<Task<bool>> showSetupAsync,
        Func<bool> isAuthenticated,
        Func<Task<bool>> showLoginAsync,
        Action<Action> showMain,
        Action shutdown)
    {
        ArgumentNullException.ThrowIfNull(showSetupAsync);
        ArgumentNullException.ThrowIfNull(isAuthenticated);
        ArgumentNullException.ThrowIfNull(showLoginAsync);
        ArgumentNullException.ThrowIfNull(showMain);
        ArgumentNullException.ThrowIfNull(shutdown);

        if (setupRequired && !await showSetupAsync())
        {
            shutdown();
            return;
        }

        if (!isAuthenticated() && !await showLoginAsync())
        {
            shutdown();
            return;
        }

        showMain(shutdown);
    }
}
