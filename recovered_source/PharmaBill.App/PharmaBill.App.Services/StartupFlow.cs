using System;
using System.Threading.Tasks;

namespace PharmaBill.App.Services;

public static class StartupFlow
{
	public static async Task RunAsync(bool setupRequired, Func<Task<bool>> showSetupAsync, Func<bool> isAuthenticated, Func<Task<bool>> showLoginAsync, Action<Action> showMain, Action shutdown)
	{
		ArgumentNullException.ThrowIfNull(showSetupAsync, "showSetupAsync");
		ArgumentNullException.ThrowIfNull(isAuthenticated, "isAuthenticated");
		ArgumentNullException.ThrowIfNull(showLoginAsync, "showLoginAsync");
		ArgumentNullException.ThrowIfNull(showMain, "showMain");
		ArgumentNullException.ThrowIfNull(shutdown, "shutdown");
		bool flag = setupRequired;
		if (flag)
		{
			flag = !(await showSetupAsync());
		}
		if (flag)
		{
			shutdown();
			return;
		}
		flag = !isAuthenticated();
		if (flag)
		{
			flag = !(await showLoginAsync());
		}
		if (flag)
		{
			shutdown();
		}
		else
		{
			showMain(shutdown);
		}
	}
}
