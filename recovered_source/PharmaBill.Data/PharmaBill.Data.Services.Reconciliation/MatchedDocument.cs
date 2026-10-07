using System;

namespace PharmaBill.Data.Services.Reconciliation;

public sealed record MatchedDocument(Guid DocumentId, string DocumentType, string DocumentNo, decimal BalanceAmount);
