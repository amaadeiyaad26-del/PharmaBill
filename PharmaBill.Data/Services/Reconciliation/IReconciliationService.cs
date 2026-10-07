using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PharmaBill.Data.Services.Reconciliation;

public interface IReconciliationService
{
	Task<ReconciliationResult> MatchAsync(IReadOnlyList<BankStatementEntry> bankLines, CancellationToken cancellationToken = default(CancellationToken));

	Task<BatchSettleResult> BatchSettleConfirmedMatchesAsync(IReadOnlyList<ReconciliationMatch> confirmedMatches, Guid userId, CancellationToken cancellationToken = default(CancellationToken));
}
