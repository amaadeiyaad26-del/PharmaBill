using System;
using System.ComponentModel;
using System.Diagnostics;
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

	public AddStockViewModel ViewModel => _viewModel;

	public AddStockWindow(AddStockViewModel viewModel, IConfirmationService confirmation)
	{
		AddStockWindow addStockWindow = this;
		InitializeComponent();
		_viewModel = viewModel;
		_confirmation = confirmation;
		DataContext = viewModel;
		viewModel.PropertyChanged += OnViewModelPropertyChanged;
		viewModel.Saved += OnSaved;
		Loaded += (object _, RoutedEventArgs _) =>
		{
			addStockWindow.FocusField(viewModel.IsManualMedicine ? "Medicine" : "Schedule");
		};
	}

	private void OnSaved(object? sender, AddStockResult result)
	{
		_confirmation.Notify("Stock added", $"Stock saved. Total stock of {_viewModel.MedicineName} is now {result.TotalStock:0.##}.");
		DialogResult = true;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == "FirstInvalidField" && _viewModel.FirstInvalidField.Length > 0)
		{
			FocusField(_viewModel.FirstInvalidField);
		}
	}

	private void FocusField(string name)
	{
		if (FindName("Field_" + name) is FloatingField { Content: UIElement content })
		{
			content.Focus();
		}
	}

}
