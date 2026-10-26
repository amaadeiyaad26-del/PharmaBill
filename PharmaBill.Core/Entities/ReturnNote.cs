
namespace PharmaBill.Core.Entities;

public sealed class ReturnNote : EntityBase
{
	public string ReturnNo { get; set; } = string.Empty;

	public string SourceType { get; set; } = string.Empty;

	public Guid SourceId { get; set; }

	public DateTime ReturnAtUtc { get; set; } = BusinessClock.UtcNow;

	public decimal TotalAmount { get; set; }

	public string Reason { get; set; } = string.Empty;

	public string? Notes { get; set; }

	/// <summary>Set when a billing terminal issued the return: its ID for that request, so a retry never credits twice. Unique when present.</summary>
	public Guid? ClientRequestId { get; set; }
}
