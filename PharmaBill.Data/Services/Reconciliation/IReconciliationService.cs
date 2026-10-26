
namespace PharmaBill.Data.Services.Reconciliation;

public interface IReconciliationService
{
	Task<ReconciliationResult> MatchAsync(IReadOnlyList<BankStatementEntry> bankLines, CancellationToken cancellationToken = default(CancellationToken));

	/// <summary>Matches one bank line by hand to a chemist, allocating their oldest open invoices.</summary>
	Task<ReconciliationMatch> MatchToCustomerAsync(BankStatementEntry line, Guid customerId, CancellationToken cancellationToken = default(CancellationToken));

	Task<BatchSettleResult> BatchSettleConfirmedMatchesAsync(IReadOnlyList<ReconciliationMatch> confirmedMatches, Guid userId, CancellationToken cancellationToken = default(CancellationToken));
}
