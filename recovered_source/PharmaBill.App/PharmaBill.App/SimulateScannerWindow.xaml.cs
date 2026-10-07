using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace PharmaBill.App;

public partial class SimulateScannerWindow : Window, IComponentConnector
{
	public static readonly IReadOnlyList<SampleBarcode> CatalogStyleSamples = new _003C_003Ez__ReadOnlyArray<SampleBarcode>(new SampleBarcode[6]
	{
		new SampleBarcode("8901030742345", "Augmentin 625 Duo Tablet"),
		new SampleBarcode("8901030123456", "Azithral 500 Tablet"),
		new SampleBarcode("8901030987654", "Ascoril LS Syrup"),
		new SampleBarcode("8901030555123", "Allegra 120mg Tablet"),
		new SampleBarcode("8901030333444", "Avil 25 Tablet"),
		new SampleBarcode("BATCH-DEMO-001", "Batch number scan key")
	});

	public string? ResultBarcode { get; private set; }

	public SimulateScannerWindow()
	{
		InitializeComponent();
		SampleList.ItemsSource = CatalogStyleSamples;
		BarcodeBox.Text = CatalogStyleSamples[0].Code;
		Loaded += (object _, RoutedEventArgs _) =>
		{
			BarcodeBox.Focus();
			BarcodeBox.SelectAll();
		};
	}

	private void SampleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (SampleList.SelectedItem is SampleBarcode sampleBarcode)
		{
			BarcodeBox.Text = sampleBarcode.Code;
		}
	}

	private void SampleList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		Simulate_Click(sender, e);
	}

	private void Simulate_Click(object sender, RoutedEventArgs e)
	{
		string text = BarcodeBox.Text?.Trim() ?? string.Empty;
		if (text.Length < 4)
		{
			MessageBox.Show(this, "Enter a barcode with at least 4 characters.", "Simulate Scanner", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		ResultBarcode = text;
		DialogResult = true;
		Close();
	}

	private void Cancel_Click(object sender, RoutedEventArgs e)
	{
		DialogResult = false;
		Close();
	}
}
