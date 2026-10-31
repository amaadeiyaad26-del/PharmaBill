using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using PharmaBill.Data.Persistence;

namespace PharmaBill.App;

/// <summary>
/// Store edition, before anything opens a database: choose the pharmacy data folder (first run, moving this
/// installation's own earlier data out of the package folder), or deal with a chosen folder that is missing or unreadable.
/// When records made by another installation of Pharma Remit are found, nothing is copied: the pharmacist is told what
/// was found and chooses to take them over, to start a new pharmacy elsewhere, or to close and uninstall one version.
/// The app never opens on an empty database because the folder could not be found.
/// </summary>
public partial class DataLocationWindow : Window
{
	private readonly string _localRoot;

	private readonly string? _ownPrivateRoot;

	private readonly bool _choosing;

	private readonly IReadOnlyList<string> _otherRecords;

	/// <summary>The data folder to use once the window closes with DialogResult true.</summary>
	public string? DataRoot { get; private set; }

	/// <summary>Which first-run choice was made (null when none, or not the other-installation case).</summary>
	public DataLocationOutcome? Outcome { get; private set; }

	public DataLocationWindow(string localRoot, DataLocationCheck check)
		: this(localRoot, check, ownPrivateRoot: null, websiteVersionInstalled: false)
	{
	}

	/// <param name="ownPrivateRoot">Where Windows physically keeps the files this Store package created under %LocalAppData%; null when not running as a package.</param>
	/// <param name="websiteVersionInstalled">The website version of Pharma Remit is installed on this computer as well.</param>
	public DataLocationWindow(string localRoot, DataLocationCheck check, string? ownPrivateRoot, bool websiteVersionInstalled)
	{
		_localRoot = localRoot;
		_ownPrivateRoot = ownPrivateRoot;
		_choosing = check.Status == DataLocationStatus.NotChosen;
		_otherRecords = _choosing ? StoreDataLocation.OtherInstallationDatabases(localRoot, ownPrivateRoot) : [];
		InitializeComponent();
		try
		{
			Icon = BitmapFrame.Create(new Uri("pack://application:,,,/PharmaBill.App;component/Assets/Icons/app.ico"));
		}
		catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
		{
			// Only the title-bar icon; the window works without it.
		}

		if (_choosing && _otherRecords.Count > 0)
		{
			ShowOtherInstallation(websiteVersionInstalled);
		}
		else if (_choosing)
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

	/// <summary>True when this window is asking what to do with another installation's records.</summary>
	public bool IsOtherInstallationChoice => _choosing && _otherRecords.Count > 0;

	private void ShowOtherInstallation(bool websiteVersionInstalled)
	{
		string folder = Path.GetFullPath(_localRoot);
		List<string> pharmacies = _otherRecords.Select(path => Path.GetFileName(Path.GetDirectoryName(path)!))
			.Where(name => !string.Equals(name, "data", StringComparison.OrdinalIgnoreCase)).ToList();
		if (_otherRecords.Any(path => string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "data", StringComparison.OrdinalIgnoreCase)))
		{
			pharmacies.Add("the main database");
		}
		HeadingText.Text = "Records from another installation of Pharma Remit were found";
		ExplanationText.Text = "Pharma Remit records are already on this computer, in " + folder + " (" + string.Join(", ", pharmacies) + "). "
			+ "This copy of Pharma Remit did not make them: they belong to another installation"
			+ (websiteVersionInstalled
				? ". This computer appears to have both the Microsoft Store version and the website version of Pharma Remit installed."
				: ", most likely the website version of Pharma Remit.");
		OtherInstallationWarning.Text = "Running both versions will produce two sets of bills and two stock levels that disagree, with the same invoice "
			+ "numbers in each. Nothing has been copied or changed.";
		TakeOverDetail.Text = "Windows does not let this Microsoft Store version keep records inside " + folder + " safely, so they are copied to the "
			+ "folder below and checked file by file. From then on the other installation refuses to open the originals, so only one set is "
			+ "ever in use; the originals are kept untouched as a safety copy. This app gets its own device identity: phones and billing "
			+ "counters paired with the other installation must be paired again.";
		OtherInstallationPanel.Visibility = Visibility.Visible;
		FolderPanel.Visibility = Visibility.Collapsed;
		FolderBox.Text = StoreDataLocation.DefaultFolder();
		PrimaryButton.Content = "Continue";
		PrimaryButton.IsEnabled = false;
		PrimaryButton.IsDefault = false;
	}

	private void Option_Checked(object sender, RoutedEventArgs e)
	{
		ErrorText.Text = string.Empty;
		bool needsFolder = TakeOverOption.IsChecked == true || StartNewOption.IsChecked == true;
		FolderPanel.Visibility = needsFolder ? Visibility.Visible : Visibility.Collapsed;
		FolderLabel.Text = TakeOverOption.IsChecked == true ? "Folder to keep these records in (must be empty)" : "Folder for the new pharmacy (must be empty)";
		PrimaryButton.Content = TakeOverOption.IsChecked == true ? "Use these records here"
			: StartNewOption.IsChecked == true ? "Start a new pharmacy"
			: "Close Pharma Remit";
		PrimaryButton.IsEnabled = true;
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
			if (IsOtherInstallationChoice)
			{
				ChooseForOtherInstallation();
			}
			else if (_choosing)
			{
				if (string.IsNullOrWhiteSpace(FolderBox.Text))
				{
					ErrorText.Text = "Choose a folder.";
					return;
				}

				DataLocationResult result = StoreDataLocation.Choose(_localRoot, FolderBox.Text.Trim(), _ownPrivateRoot);
				DataRoot = result.DataRoot;
				Outcome = result.Outcome;
				Finish(true);
			}
			else
			{
				string? folder = PickFolder("Choose the folder that holds your pharmacy data");
				if (folder == null)
				{
					return;
				}

				DataRoot = StoreDataLocation.Relocate(_localRoot, folder).DataRoot;
				Finish(true);
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

	private void ChooseForOtherInstallation()
	{
		if (CloseOption.IsChecked == true)
		{
			Outcome = null;
			Finish(false);
			return;
		}
		if (string.IsNullOrWhiteSpace(FolderBox.Text))
		{
			ErrorText.Text = "Choose a folder.";
			return;
		}
		DataLocationResult result = TakeOverOption.IsChecked == true
			? StoreDataLocation.TakeOver(_localRoot, FolderBox.Text.Trim(), _ownPrivateRoot)
			: StoreDataLocation.StartNew(_localRoot, FolderBox.Text.Trim());
		DataRoot = result.DataRoot;
		Outcome = result.Outcome;
		Finish(true);
	}

	private void Retry_Click(object sender, RoutedEventArgs e)
	{
		DataLocationCheck check = StoreDataLocation.Check(_localRoot);
		if (check.Status == DataLocationStatus.Ready)
		{
			DataRoot = check.DataRoot;
			Finish(true);
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

	private void Exit_Click(object sender, RoutedEventArgs e) => Finish(false);

	/// <summary>How the window was closed: true with <see cref="DataRoot"/> set, false to exit.</summary>
	public bool? Result { get; private set; }

	private void Finish(bool result)
	{
		Result = result;
		try
		{
			DialogResult = result;
		}
		catch (InvalidOperationException)
		{
			// Shown without ShowDialog (tests): close it the ordinary way.
			Close();
		}
	}
}
