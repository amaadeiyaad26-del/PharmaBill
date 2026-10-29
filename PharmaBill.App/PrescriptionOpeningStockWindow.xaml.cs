using PharmaBill.Core;
using System.Globalization;
using System.Windows;
using PharmaBill.App.ViewModels;

namespace PharmaBill.App;

public partial class PrescriptionOpeningStockWindow : Window
{
	public OpeningStockDraft? Draft { get; private set; }

	public PrescriptionOpeningStockWindow(string brandName, decimal quantity)
	{
		InitializeComponent();
		BrandBox.Text = brandName?.Trim() ?? string.Empty;
		// Never a made-up date: the expiry comes from the pack, or the tick says the pack shows none.
		ExpiryBox.Text = string.Empty;
		Loaded += (_, _) => ExpiryBox.Focus();
		QtyBox.Text = (quantity > 0m ? quantity : 1m).ToString("0.##", CultureInfo.CurrentCulture);
		MrpBox.Text = "0";
		RateBox.Text = "0";
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		StatusText.Text = string.Empty;
		string brand = BrandBox.Text.Trim();
		if (brand.Length == 0)
		{
			StatusText.Text = "Brand name is required.";
			BrandBox.Focus();
			return;
		}

		string batch = BatchBox.Text.Trim();
		if (batch.Length == 0)
		{
			StatusText.Text = "Batch number is required.";
			BatchBox.Focus();
			return;
		}

		DateOnly? expiry = null;
		if (ExpiryBox.Text.Trim().Length > 0)
		{
			if (!PharmaBill.Core.Inventory.ExpiryRule.TryParseTyped(ExpiryBox.Text, out DateOnly parsed))
			{
				StatusText.Text = "Type the expiry as MM/YY, for example 10/27.";
				ExpiryBox.Focus();
				return;
			}
			expiry = parsed;
		}
		else if (NoExpiryBox.IsChecked != true)
		{
			StatusText.Text = "Type the expiry from the pack (MM/YY), or tick \"No expiry printed on pack\".";
			ExpiryBox.Focus();
			return;
		}

		if (!TryAmount(MrpBox.Text, out decimal mrp) || mrp < 0m)
		{
			StatusText.Text = "Enter a valid MRP.";
			MrpBox.Focus();
			return;
		}

		if (!TryAmount(RateBox.Text, out decimal rate) || rate < 0m)
		{
			StatusText.Text = "Enter a valid purchase rate.";
			RateBox.Focus();
			return;
		}

		if (!TryAmount(QtyBox.Text, out decimal quantity) || quantity <= 0m)
		{
			StatusText.Text = "Enter an inward quantity above zero.";
			QtyBox.Focus();
			return;
		}

		Draft = new OpeningStockDraft(brand, batch, expiry, mrp, rate, quantity, NoExpiryPrinted: expiry == null && NoExpiryBox.IsChecked == true);
		DialogResult = true;
	}

	private static bool TryAmount(string? text, out decimal value)
	{
		string raw = (text ?? string.Empty).Trim();
		return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
			|| decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
	}
}
