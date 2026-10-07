using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App.Views;

public partial class WholesaleReconciliationView : UserControl, IComponentConnector
{
	public WholesaleReconciliationView()
	{
		InitializeComponent();
	}

	private void OnDragOver(object sender, DragEventArgs e)
	{
		e.Effects = (e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None);
		e.Handled = true;
	}

	private async void OnDrop(object sender, DragEventArgs e)
	{
		if (DataContext is WholesaleReconciliationPageViewModel wholesaleReconciliationPageViewModel && e.Data.GetData(DataFormats.FileDrop) is string[] array && array.Length != 0)
		{
			string text = array.FirstOrDefault((string file) => Path.GetExtension(file).Equals(".csv", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(file).Equals(".xlsx", StringComparison.OrdinalIgnoreCase));
			if (text == null)
			{
				wholesaleReconciliationPageViewModel.ErrorMessage = "Drop a CSV or XLSX bank statement.";
			}
			else
			{
				await wholesaleReconciliationPageViewModel.ImportFromPathAsync(text);
			}
		}
	}
}
