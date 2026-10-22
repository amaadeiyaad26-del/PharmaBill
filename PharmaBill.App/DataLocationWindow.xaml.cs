using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App;

/// <summary>
/// Store edition, before anything opens a database: choose the pharmacy data folder (first run, moving existing data
/// out of the package folder), or deal with a chosen folder that is missing or unreadable. The app never opens on an
/// empty database because the folder could not be found.
/// </summary>
public partial class DataLocationWindow : Window
{
	private readonly string _localRoot;

	private readonly bool _choosing;

	/// <summary>The data folder to use once the window closes with DialogResult true.</summary>
	public string? DataRoot { get; private set; }

	public DataLocationWindow(string localRoot, DataLocationCheck check)
	{
		_localRoot = localRoot;
		_choosing = check.Status == DataLocationStatus.NotChosen;
		InitializeComponent();
		if (_choosing)
		{
			bool hasData = StoreDataLocation.ContainsDatabase(localRoot);
			HeadingText.Text = hasData ? "Move your pharmacy data to a safe folder" : "Where should Pharma Remit keep your pharmacy data?";
			ExplanationText.Text = "Bills, purchase records and the Schedule H register must be kept for years. Windows deletes an app's own "
				+ "data folder when the app is uninstalled, so Pharma Remit keeps your records in a folder you choose instead. "
				+ "Include this folder in your regular backups.";
			FolderBox.Text = StoreDataLocation.DefaultFolder();
			MigrationNote.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
			PrimaryButton.Content = hasData ? "Move data here" : "Use this folder";
		}
		else
		{
			HeadingText.Text = check.Status == DataLocationStatus.DatabaseMissing
				? "Your pharmacy database is missing"
				: "Your pharmacy data folder can't be opened";
			ExplanationText.Text = check.Message + " Pharma Remit has not started, so nothing is shown or recorded against an empty database.";
			FolderBox.Text = check.DataRoot ?? string.Empty;
			FolderBox.IsReadOnly = true;
			BrowseButton.Visibility = Visibility.Collapsed;
			RetryButton.Visibility = Visibility.Visible;
			PrimaryButton.Content = "Locate folder...";
			PrimaryButton.IsDefault = false;
			RetryButton.IsDefault = true;
		}
	}

	private string? PickFolder(string title)
	{
		OpenFolderDialog dialog = new OpenFolderDialog { Title = title, Multiselect = false };
		string start = Directory.Exists(FolderBox.Text) ? FolderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		dialog.InitialDirectory = start;
		return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
	}

	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		string? folder = PickFolder("Choose where to keep your pharmacy data");
		if (folder != null)
		{
			FolderBox.Text = folder;
		}
	}

	private void Primary_Click(object sender, RoutedEventArgs e)
	{
		ErrorText.Text = string.Empty;
		Mouse.OverrideCursor = Cursors.Wait;
		try
		{
			if (_choosing)
			{
				if (string.IsNullOrWhiteSpace(FolderBox.Text))
				{
					ErrorText.Text = "Choose a folder.";
					return;
				}

				DataRoot = StoreDataLocation.Choose(_localRoot, FolderBox.Text.Trim()).DataRoot;
				DialogResult = true;
			}
			else
			{
				string? folder = PickFolder("Choose the folder that holds your pharmacy data");
				if (folder == null)
				{
					return;
				}

				DataRoot = StoreDataLocation.Relocate(_localRoot, folder).DataRoot;
				DialogResult = true;
			}
		}
		catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
		{
			ErrorText.Text = PharmaBill.Core.UserMessage.Of(ex);
		}
		finally
		{
			Mouse.OverrideCursor = null;
		}
	}

	private void Retry_Click(object sender, RoutedEventArgs e)
	{
		DataLocationCheck check = StoreDataLocation.Check(_localRoot);
		if (check.Status == DataLocationStatus.Ready)
		{
			DataRoot = check.DataRoot;
			DialogResult = true;
			return;
		}

		ErrorText.Text = check.Message;
	}

	private void FolderBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
	{
		string? provider = null;
		try
		{
			provider = _choosing && !string.IsNullOrWhiteSpace(FolderBox.Text) ? StoreDataLocation.CloudSyncProvider(FolderBox.Text.Trim()) : null;
		}
		catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
		{
			// Not a valid path yet while typing.
		}

		SyncWarningText.Text = provider == null ? string.Empty
			: $"This folder is synced by {provider}. A database that {provider} is syncing can be damaged or split into conflicting copies. "
				+ $"Prefer a folder {provider} does not sync, such as {StoreDataLocation.DefaultFolder()}, and back it up separately.";
		SyncWarning.Visibility = provider == null ? Visibility.Collapsed : Visibility.Visible;
	}
	private void Exit_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
