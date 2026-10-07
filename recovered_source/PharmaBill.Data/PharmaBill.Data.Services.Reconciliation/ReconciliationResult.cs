using System.Collections.Generic;

namespace PharmaBill.Data.Services.Reconciliation;

public sealed record ReconciliationResult(IReadOnlyList<ReconciliationMatch> Matches, int HighCount, int MediumCount, int LowCount, int UnmatchedCount);
