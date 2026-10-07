using System;

namespace PharmaBill.Core.Security;

public sealed record TrialEvaluation(DateTime TrialStartedAtUtc, bool ClockMovedBackwards, TimeSpan Remaining, bool IsTrialActive);
