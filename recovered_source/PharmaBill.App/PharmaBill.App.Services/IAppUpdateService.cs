using System;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.App.Services;

public interface IAppUpdateService
{
	AppUpdateAvailability? LastResult { get; }

	event EventHandler<AppUpdateAvailability>? UpdateStateChanged;

	Task<AppUpdateAvailability> CheckForUpdatesAsync(bool force = false, CancellationToken cancellationToken = default(CancellationToken));

	Task LaunchUpdateAsync(CancellationToken cancellationToken = default(CancellationToken));

	void RemindTomorrow();

	bool IsSnoozed();
}
