using System.Windows;
using System.Windows.Markup;
using PharmaBill.App.ViewModels;
using PharmaBill.Sync;

namespace PharmaBill.App;

public partial class DiagnosticsWindow : Window, IComponentConnector
{
	private readonly DiagnosticsViewModel _viewModel;

	private readonly CloudOAuthClientStore _oauthClients;

	public DiagnosticsWindow(DiagnosticsViewModel viewModel, CloudOAuthClientStore oauthClients)
	{
		_viewModel = viewModel;
		_oauthClients = oauthClients;
		InitializeComponent();
		DataContext = viewModel;
		Loaded += OnLoaded;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		await _viewModel.RefreshCommand.ExecuteAsync(null);
	}

	private void ConfigureCloudKeys_Click(object sender, RoutedEventArgs e)
	{
		CloudKeysWindow cloudKeysWindow = new CloudKeysWindow(_oauthClients);
		cloudKeysWindow.Owner = this;
		cloudKeysWindow.ShowDialog();
	}
}
