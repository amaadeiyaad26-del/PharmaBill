using System;

namespace PharmaBill.Core.Ai;

public sealed record DrugNameMatch(Guid DrugId, string MatchedName, double Score);
