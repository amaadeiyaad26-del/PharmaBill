using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.Services;

// "Search data bank": fills a new medicine's details from this shop's own medicines and the built-in medicine data bank
// (about 2.5 lakh Indian brands). It works offline and sends nothing anywhere. The Gemini / OpenRouter / OpenFDA online
// lookup was removed on 22 Oct 2026 at the owner's request.
public sealed class SmartDrugLookupService(IServiceScopeFactory scopes, ILogger<SmartDrugLookupService>? logger = null) : ISmartDrugLookupService
{
	public async Task<SmartDrugSuggestion?> TryLookupAsync(string query, CancellationToken cancellationToken = default)
	{
		SmartDrugLookupOutcome outcome = await LookupAsync(query, cancellationToken);
		return outcome.Suggestion;
	}

	public async Task<SmartDrugLookupOutcome> LookupAsync(string query, CancellationToken cancellationToken = default)
	{
		string typed = query?.Trim() ?? string.Empty;
		if (typed.Length < 2)
		{
			return new SmartDrugLookupOutcome(null, "Type at least 2 letters of the medicine name, then search the data bank.");
		}

		try
		{
			using IServiceScope scope = scopes.CreateScope();
			PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			SmartDrugSuggestion? found = await LocalDrugSuggestionLookup.FindAsync(context, typed, cancellationToken);
			if (found == null)
			{
				return new SmartDrugLookupOutcome(null, SmartDrugLookupPrompt.NotFoundNotice(typed));
			}
			bool taxKnown = !string.IsNullOrWhiteSpace(found.HsnCode) && (found.IsNilRated || found.GstRate is > 0m);
			return new SmartDrugLookupOutcome(found, taxKnown ? SmartDrugLookupPrompt.FilledWithTaxNotice : SmartDrugLookupPrompt.FilledNotice);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{
			logger?.LogWarning(exception, "Medicine data bank search failed for {Query}", typed);
			return new SmartDrugLookupOutcome(null, "The medicine data bank could not be searched just now. Type the details from the pack or the supplier invoice.");
		}
	}
}
