using System.Windows;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class DiagnosticsWindow : Window
{
    private readonly DiagnosticsViewModel _viewModel;

    public DiagnosticsWindow(DiagnosticsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.RefreshCommand.ExecuteAsync(null);
}
