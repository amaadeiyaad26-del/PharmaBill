using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class UserManualWindow : Window, IComponentConnector
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

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.12.0")]
	internal Delegate _CreateDelegate(Type delegateType, string handler)
	{
		return Delegate.CreateDelegate(delegateType, this, handler);
	}
}
