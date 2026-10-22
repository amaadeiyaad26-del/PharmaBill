using System;
using System.Windows;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

/// <summary>The whole app on a billing-counter PC: sign in, bill, recent bills. No local records are opened.</summary>
public partial class TerminalWindow : Window
{
	public TerminalWindow(TerminalViewModel viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
		Closed += (_, _) => viewModel.Dispose();
	}
}
