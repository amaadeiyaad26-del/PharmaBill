using System.ComponentModel;
using System.Windows;
using System.Windows.Markup;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class CatalogImportWindow : Window, IComponentConnector
{
	private readonly CatalogImportViewModel _viewModel;

	public CatalogImportWindow(CatalogImportViewModel viewModel)
	{
		_viewModel = viewModel;
		InitializeComponent();
		DataContext = viewModel;
		Loaded += OnLoaded;
		Closing += OnClosing;
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		await _viewModel.StartCommand.ExecuteAsync(null);
	}

	private void Continue_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = true;
	}

	private void OnClosing(object? sender, CancelEventArgs e)
	{
		if (_viewModel.IsImporting)
		{
			e.Cancel = true;
			_viewModel.CancelCommand.Execute(null);
		}
	}
}
