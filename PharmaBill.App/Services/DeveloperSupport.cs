using System.Diagnostics;
using System.Windows;

namespace PharmaBill.App.Services;

public static class DeveloperSupport
{
	public static void ContactAndCopyEmail()
	{
		try
		{
			Clipboard.SetText("pharma.bill26@gmail.com");
		}
		catch
		{
		}
		try
		{
			Process.Start(new ProcessStartInfo("mailto:pharma.bill26@gmail.com?subject=PharmaBill%20Support%20Request")
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}
}
