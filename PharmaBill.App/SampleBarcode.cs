namespace PharmaBill.App;

public sealed record SampleBarcode(string Code, string Label)
{
	public string Display => Code + "  —  " + Label;
}
