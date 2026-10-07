namespace PharmaBill.Core.Entities;

public sealed class NumberSeries : EntityBase
{
	public string SeriesPrefix { get; set; } = string.Empty;

	public string FinancialYear { get; set; } = string.Empty;

	public long LastNumber { get; set; }
}
