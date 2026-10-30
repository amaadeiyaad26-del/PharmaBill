namespace PharmaBill.App.Services;

public enum UpdateChannel
{
	/// <summary>Nothing could be asked: offline, or the update server did not answer.</summary>
	None,
	MicrosoftStore,
	/// <summary>The Local Market edition's update file on pharmaremit.com.</summary>
	Website
}
