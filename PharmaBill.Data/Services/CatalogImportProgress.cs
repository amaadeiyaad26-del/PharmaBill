using System;

namespace PharmaBill.Data.Services;

public sealed record CatalogImportProgress(string ImportKey, string FileName, long ProcessedRows, long ImportedRows, long SkippedRows, long ExpectedRows, int Percentage, TimeSpan EstimatedTimeRemaining, string Status, string? LastError);
