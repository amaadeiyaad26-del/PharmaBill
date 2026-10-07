using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using PharmaBill.Sync;

namespace PharmaBill.App;

public partial class CloudKeysWindow : Window, IComponentConnector
{
	private readonly CloudOAuthClientStore _store;

	public bool KeysApplied { get; private set; }

	public CloudKeysWindow(CloudOAuthClientStore store)
	{
		_store = store;
		InitializeComponent();
		Loaded += async (object _, RoutedEventArgs _) =>
		{
			await LoadAsync();
		};
	}

	private async Task LoadAsync()
	{
		try
		{
			GoogleOAuthClientConfig googleOAuthClientConfig = await _store.LoadDisplayAsync();
			GoogleClientIdBox.Text = googleOAuthClientConfig?.ClientId ?? string.Empty;
			GoogleClientSecretBox.Password = string.Empty;
			GoogleClientIdBox.Focus();
		}
		catch (Exception ex)
		{
			StatusText.Text = ex.Message;
		}
	}

	private async void Save_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			StatusText.Text = string.Empty;
			SaveButton.IsEnabled = false;
			await _store.SaveAdminOverridesAsync(GoogleClientIdBox.Text, GoogleClientSecretBox.Password);
			KeysApplied = true;
			DialogResult = true;
			Close();
		}
		catch (Exception exception)
		{
			StatusText.Text = CloudOAuthClientStore.ToUserFriendlyMessage(exception);
			SaveButton.IsEnabled = true;
		}
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		KeysApplied = false;
		DialogResult = false;
		Close();
	}
}
