using System.Diagnostics;
using System.IO;
using System.Windows;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App;

/// <summary>
/// Shown instead of a startup crash when the pharmacy database has content but will not open. Nothing is moved
/// unless the user explicitly chooses to start a new database, and even then the file is renamed, never deleted.
/// </summary>
public partial class DatabaseRecoveryWindow : Window
{
	private readonly DatabaseUnreadableException _problem;

	public DatabaseRecoveryWindow(DatabaseUnreadableException problem)
	{
		_problem = problem;
		InitializeComponent();
		PathText.Text = problem.DatabasePath;
		DetailText.Text = problem.Detail;
		if (problem.NewKeyCreatedThisLaunch)
		{
			HeadingText.Text = "Your records are very likely safe, but the database key is missing.";
			ExplanationText.Text = "The database is encrypted with a key kept in database.key, and that file was not found. "
				+ "Restore database.key, together with the database, from your backup (or the old computer) into the data folder, "
				+ "replacing the new database.key created today, then open Pharma Remit again. Contact support if you need help.";
		}
		else if (problem.IsUnencryptedSqlite)
		{
			HeadingText.Text = "The database file is not a Pharma Remit database.";
			ExplanationText.Text = "It is an unencrypted SQLite file, so it may have been copied in by mistake. "
				+ "Restore your Pharma Remit backup, or contact support to import its data.";
		}
		else
		{
			HeadingText.Text = "The pharmacy database could not be opened.";
			ExplanationText.Text = "The file may be damaged, or it may have been encrypted with a different key (for example on another "
				+ "Windows user account or computer). Your file has not been changed. Restore a backup, or open the app from the Windows "
				+ "user that created it.";
		}
	}

	private void ConfirmFresh_Changed(object sender, RoutedEventArgs e) => StartFreshButton.IsEnabled = ConfirmFreshBox.IsChecked == true;

	private void OpenFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string? folder = Path.GetDirectoryName(_problem.DatabasePath);
			if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
			{
				Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
			}
		}
		catch (Exception ex)
		{
			ResultText.Text = "Could not open the folder: " + PharmaBill.Core.UserMessage.Of(ex);
		}
	}

	private void StartFresh_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			string moved = DatabaseFileHealth.Quarantine(_problem.DatabasePath, "unreadable");
			StartFreshButton.IsEnabled = false;
			ConfirmFreshBox.IsEnabled = false;
			ResultText.Text = "The file was renamed to " + moved + ". Open Pharma Remit again to start with a new empty database.";
		}
		catch (Exception ex)
		{
			ResultText.Foreground = System.Windows.Media.Brushes.Firebrick;
			ResultText.Text = "Could not rename the file (" + PharmaBill.Core.UserMessage.Of(ex) + "). Close other programs using it and try again.";
		}
	}

	private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
