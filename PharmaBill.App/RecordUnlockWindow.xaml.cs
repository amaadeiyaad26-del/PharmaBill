using System.Windows;

namespace PharmaBill.App;

public partial class RecordUnlockWindow : Window
{
	public static readonly string[] ReasonOptions =
	[
		"Drug Inspector / Regulatory Correction",
		"Distributor Credit Note Adjustment",
		"Billing Typographical Error",
		"Patient Return / Audit Correction"
	];

	public RecordUnlockWindow()
	{
		InitializeComponent();
		ReasonBox.ItemsSource = ReasonOptions;
		ReasonBox.SelectedIndex = 0;
	}

	public string AdminUserName => UsernameBox.Text.Trim();

	public string Pin => PinBox.Password;

	public string ReasonCode => ReasonBox.SelectedItem as string ?? string.Empty;

	public string Notes => NotesBox.Text.Trim();

	private void Authorize_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(Pin))
		{
			ErrorText.Text = "Enter the Master Admin password or PIN.";
			return;
		}

		if (string.IsNullOrWhiteSpace(ReasonCode))
		{
			ErrorText.Text = "Select a reason for the modification.";
			return;
		}

		if (Notes.Length < 10)
		{
			ErrorText.Text = "Notes must be at least 10 characters.";
			return;
		}

		DialogResult = true;
	}
}
