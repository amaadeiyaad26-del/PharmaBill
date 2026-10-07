using System;

namespace PharmaBill.Core.Entities;

public sealed class CatalogImportState : EntityBase
{
	public string ImportKey { get; set; } = string.Empty;

	public string FilePath { get; set; } = string.Empty;

	public string FileFingerprint { get; set; } = string.Empty;

	public long LastProcessedRecord { get; set; }

	public long ImportedRows { get; set; }

	public long SkippedRows { get; set; }

	public string Status { get; set; } = "NotStarted";

	public string? LastError { get; set; }

	public DateTime? CompletedAtUtc { get; set; }
}
