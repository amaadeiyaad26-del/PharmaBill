namespace PharmaBill.Data.Services;

public sealed record CategoryShare(string Name, decimal Amount, double Percent)
{
	public string PercentText => $"{Percent:0.#}%";
}
