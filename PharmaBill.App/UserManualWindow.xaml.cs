using System;
using System.Diagnostics;
using System.Windows;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class UserManualWindow : Window
{
	public UserManualWindow(UserManualViewModel viewModel)
	{
		UserManualWindow userManualWindow = this;
		InitializeComponent();
		DataContext = viewModel;
		viewModel.CloseRequested += OnCloseRequested;
		Closed += (object? _, EventArgs _) =>
		{
			viewModel.CloseRequested -= userManualWindow.OnCloseRequested;
		};
	}

	private void OnCloseRequested(object? sender, EventArgs e)
	{
		Close();
	}

	protected override void OnContentRendered(EventArgs e)
	{
		base.OnContentRendered(e);
		ChapterSearchBox.Focus();
	}

}
