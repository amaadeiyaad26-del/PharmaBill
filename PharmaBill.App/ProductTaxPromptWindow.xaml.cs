using System.Windows;
using PharmaBill.App.Services;
using PharmaBill.Core.Gst;

namespace PharmaBill.App;

/// <summary>Asks for a product's HSN code and GST rate (or Nil-rated) when a bill line has none recorded.</summary>
public partial class ProductTaxPromptWindow : Window
{
	public ProductTaxDetails? Details { get; private set; }

	public bool SaveToProduct => SaveToProductBox.IsChecked == true;

	public ProductTaxPromptWindow(string productName, string? currentHsn, decimal? currentRate, bool currentNilRated, bool newProduct = false)
	{
		InitializeComponent();
		string missing = ProductTax.DescribeMissing(currentHsn, currentRate, currentNilRated) ?? "HSN code and GST rate";
		MessageText.Text = newProduct
			? $"{productName} is a new medicine. Enter its HSN code and GST rate to save it."
			: $"{productName} has no {missing} recorded. Enter it to bill this line.";
		if (newProduct)
		{
			// A new product always gets these values; there is no bill-only option.
			SaveToProductBox.IsChecked = true;
			SaveToProductBox.Visibility = Visibility.Collapsed;
			LaterButton.Content = "Cancel";
		}
		HsnBox.Text = currentHsn?.Trim() ?? string.Empty;
		// A stored 0% that was never marked Nil-rated is not a known rate, so it is not shown as one.
		RateBox.Text = currentRate is > 0m ? currentRate.Value.ToString("0.##") : string.Empty;
		NilRatedBox.IsChecked = currentNilRated;
		Loaded += (_, _) =>
		{
			if (string.IsNullOrWhiteSpace(HsnBox.Text))
			{
				HsnBox.Focus();
			}
			else
			{
				RateBox.Focus();
			}
		};
	}

	private void NilRated_Changed(object sender, RoutedEventArgs e)
	{
		bool nil = NilRatedBox.IsChecked == true;
		RateBox.IsEnabled = !nil;
		RateField.IsRequired = !nil;
		if (nil)
		{
			RateBox.Text = string.Empty;
		}
	}

	private void Ok_Click(object sender, RoutedEventArgs e)
	{
		bool nil = NilRatedBox.IsChecked == true;
		if (!GstRateText.TryParse(RateBox.Text, out decimal? rate))
		{
			ErrorText.Text = "GST rate must be a number, for example 5 or 12.";
			RateBox.Focus();
			return;
		}

		if (!ProductTax.TryCreate(HsnBox.Text, rate, nil, out ProductTaxDetails? details, out string? error))
		{
			ErrorText.Text = error;
			return;
		}

		Details = details;
		DialogResult = true;
	}
}
