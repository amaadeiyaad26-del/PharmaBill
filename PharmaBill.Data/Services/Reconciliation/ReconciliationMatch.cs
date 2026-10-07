using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services.Reconciliation;

public sealed record ReconciliationMatch(BankStatementEntry BankLine, Guid? CustomerId, string? CustomerName, IReadOnlyList<MatchedDocument> MatchedDocuments, double ConfidencePercent, ReconciliationConfidence Confidence, string MatchReason, bool IsConfirmed);
