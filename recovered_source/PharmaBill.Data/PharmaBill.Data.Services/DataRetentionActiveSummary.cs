using System;
using PharmaBill.Core;

namespace PharmaBill.Data.Services;

public sealed record DataRetentionActiveSummary(long ActiveRecordCount, DateTime? OldestActiveUtc, DateTime CutoffUtc, DataRetentionPeriod Period, long CandidateArchiveCount);
