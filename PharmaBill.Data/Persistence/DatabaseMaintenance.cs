using System.Threading;

namespace PharmaBill.Data.Persistence;

/// <summary>
/// Held while the database file itself is being replaced or deleted (restore, a fresh store), so background work that
/// merely keeps the database warm never opens it mid-swap: opening a missing file would create an empty database.
/// </summary>
public static class DatabaseMaintenance
{
	private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

	/// <summary>Waits for and holds the gate until the returned handle is disposed.</summary>
	public static async Task<IDisposable> HoldAsync(CancellationToken cancellationToken = default)
	{
		await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
		return new Release();
	}

	/// <summary>Takes the gate only if nothing is replacing the database right now.</summary>
	public static bool TryEnter() => Gate.Wait(0);

	public static void Exit() => Gate.Release();

	private sealed class Release : IDisposable
	{
		private int _released;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0)
			{
				Gate.Release();
			}
		}
	}
}
