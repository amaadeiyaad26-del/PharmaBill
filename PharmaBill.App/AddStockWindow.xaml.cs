using System.ComponentModel;
using System.Windows;
using PharmaBill.App.Controls;
using PharmaBill.App.Services;
using PharmaBill.App.ViewModels;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class AddStockWindow : Window
{
    private readonly AddStockViewModel _viewModel;
    private readonly IConfirmationService _confirmation;

    public AddStockWindow(AddStockViewModel viewModel, IConfirmationService confirmation)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _confirmation = confirmation;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Saved += OnSaved;
        Loaded += (_, _) => FocusField(viewModel.IsManualMedicine ? "Medicine" : "Schedule");
    }

    public AddStockViewModel ViewModel => _viewModel;

    private void OnSaved(object? sender, AddStockResult result)
    {
        _confirmation.Notify(
            "Stock added",
            $"Stock saved. Total stock of {_viewModel.MedicineName} is now {result.TotalStock:0.##}.");
        DialogResult = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddStockViewModel.FirstInvalidField) &&
            _viewModel.FirstInvalidField.Length > 0)
        {
            FocusField(_viewModel.FirstInvalidField);
        }
    }

    private void FocusField(string name)
    {
        if (FindName($"Field_{name}") is FloatingField { Content: UIElement element })
        {
            element.Focus();
        }
    }
}