using System;
using PharmaBill.Core;

namespace PharmaBill.Data.Services;

public sealed record DataRetentionSettings(DataRetentionPeriod Period, DateTime? LastArchiveAtUtc, string? LastArchivePath, long LastArchivedRowCount);
