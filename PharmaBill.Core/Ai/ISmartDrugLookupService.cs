using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Core.Ai;

public interface ISmartDrugLookupService
{
	/// <summary>
	/// Looks up a typed medicine name. Returns null when the machine is offline,
	/// the provider is not configured, the request times out, or the reply cannot be read.
	/// </summary>
	Task<SmartDrugSuggestion?> TryLookupAsync(string query, CancellationToken cancellationToken = default);

	/// <summary>
	/// Looks up a typed medicine name and always returns a chemist-facing reason,
	/// whether the fields were filled or the search failed.
	/// </summary>
	Task<SmartDrugLookupOutcome> LookupAsync(string query, CancellationToken cancellationToken = default);
}
