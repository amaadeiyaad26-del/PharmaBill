using System;

namespace PharmaBill.Data.Services;

public sealed record AccessState(DateTime? TrialStartedAtUtc, DateTime? LastObservedUtc, bool ClockRollbackDetected = false);
