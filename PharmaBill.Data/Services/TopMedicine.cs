namespace PharmaBill.Data.Services;

public sealed record TopMedicine(int Rank, string Name, decimal Units, double Percent)
{
	public string RankText => $"#{Rank}";

	public string UnitsText => $"{Units:0.##} units";

	public string PercentText => $"{Percent:0.#}%";
}
