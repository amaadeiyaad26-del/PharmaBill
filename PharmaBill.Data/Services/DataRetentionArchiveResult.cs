using System;
using System.Collections.Generic;

namespace PharmaBill.Data.Services;

public sealed record DataRetentionArchiveResult(string ArchivePath, DateTime CutoffUtc, long ExportedRowCount, long SoftDeletedRowCount, IReadOnlyDictionary<string, long> TableCounts);
