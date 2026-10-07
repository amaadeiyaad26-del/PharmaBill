using System.Windows;

namespace PharmaBill.App;

public partial class PinPromptWindow : Window
{
	public string Prompt { get; }

	public string ErrorMessage { get; private set; } = string.Empty;

	public string Pin => PinBox.Password;

	public string Username => UsernameBox.Text.Trim();

	public bool ShowUsername
	{
		get
		{
			return UsernamePanel.Visibility == Visibility.Visible;
		}
		set
		{
			UsernamePanel.Visibility = ((!value) ? Visibility.Collapsed : Visibility.Visible);
			Title = (value ? "Admin override required" : "Sensitive records access");
		}
	}

	public PinPromptWindow(string prompt)
	{
		InitializeComponent();
		Prompt = prompt;
		DataContext = this;
		Loaded += (object _, RoutedEventArgs _) =>
		{
			if (ShowUsername)
			{
				UsernameBox.Focus();
			}
			else
			{
				PinBox.Focus();
			}
		};
	}

	private void Continue_Click(object sender, RoutedEventArgs e)
	{
		if (ShowUsername && string.IsNullOrWhiteSpace(UsernameBox.Text))
		{
			ErrorMessage = "Enter an Admin username.";
			RefreshBinding();
			UsernameBox.Focus();
		}
		else if (string.IsNullOrWhiteSpace(PinBox.Password))
		{
			ErrorMessage = "Enter your PIN or password.";
			RefreshBinding();
			PinBox.Focus();
		}
		else
		{
			DialogResult = true;
		}
	}

	private void RefreshBinding()
	{
		DataContext = null;
		DataContext = this;
	}
}
