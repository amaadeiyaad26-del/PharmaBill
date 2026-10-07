using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Services;

public sealed class UserManualDialogService : IUserManualDialogService
{
	public void ShowInteractiveGuide()
	{
		UserManualWindow userManualWindow = new UserManualWindow(new UserManualViewModel());
		userManualWindow.Owner = Application.Current?.MainWindow;
		userManualWindow.ShowDialog();
	}

	public string OpenPdfManual()
	{
		string text = ResolvePdfPath();
		if (string.IsNullOrWhiteSpace(text) || !File.Exists(text))
		{
			return "User manual PDF was not found. Reinstall PharmaBill or regenerate docs.";
		}
		Process.Start(new ProcessStartInfo
		{
			FileName = text,
			UseShellExecute = true
		});
		return "Opened PDF: " + Path.GetFileName(text);
	}

	private static string ResolvePdfPath()
	{
		foreach (string item in EnumeratePdfCandidates())
		{
			if (File.Exists(item) && new FileInfo(item).Length > 500)
			{
				return item;
			}
		}
		return UserManualPdfBuilder.EnsurePdfPath();
	}

	private static IEnumerable<string> EnumeratePdfCandidates()
	{
		string baseDir = AppContext.BaseDirectory;
		yield return Path.Combine(baseDir, "docs", "PharmaBill_User_Manual.pdf");
		yield return Path.Combine(baseDir, "Assets", "Docs", "PharmaBill_User_Manual.pdf");
		yield return Path.Combine(baseDir, "PharmaBill_User_Manual.pdf");
	}
}
