using System;

namespace PharmaBill.Core.Ai;

public sealed record PrescribedItemDto
{
	public string DrugName { get; init; } = string.Empty;

	public string Dosage { get; init; } = string.Empty;

	public string Duration { get; init; } = string.Empty;

	public Guid? MatchedCatalogProductId { get; init; }

	public double ConfidenceScore { get; init; }
}
