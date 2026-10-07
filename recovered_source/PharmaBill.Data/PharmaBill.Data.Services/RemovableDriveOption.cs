namespace PharmaBill.Data.Services;

public sealed record RemovableDriveOption(string Root, string Label, long AvailableFreeSpace, long TotalSize)
{
	public string DisplayName
	{
		get
		{
			double value = (double)AvailableFreeSpace / 1073741824.0;
			string value2 = (string.IsNullOrWhiteSpace(Label) ? "Removable drive" : Label.Trim());
			return $"{Root} ({value2}) — {value:0.0} GB free";
		}
	}
}
