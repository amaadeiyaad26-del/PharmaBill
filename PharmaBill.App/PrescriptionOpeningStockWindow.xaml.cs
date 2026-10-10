using System;
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
		DateTime expiry = DateTime.Today.AddYears(1);
		ExpiryBox.Text = expiry.ToString("MM/yyyy", CultureInfo.InvariantCulture);
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

		if (!TryParseExpiry(ExpiryBox.Text, out DateOnly expiry))
		{
			StatusText.Text = "Enter expiry as MM/YYYY.";
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

		Draft = new OpeningStockDraft(brand, batch, expiry, mrp, rate, quantity);
		DialogResult = true;
	}

	private static bool TryParseExpiry(string? text, out DateOnly expiry)
	{
		expiry = default;
		string raw = (text ?? string.Empty).Trim();
		if (!DateTime.TryParseExact(raw, new[] { "MM/yyyy", "M/yyyy", "MM/yy", "M/yy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
		{
			return false;
		}

		expiry = new DateOnly(parsed.Year, parsed.Month, DateTime.DaysInMonth(parsed.Year, parsed.Month));
		return true;
	}

	private static bool TryAmount(string? text, out decimal value)
	{
		string raw = (text ?? string.Empty).Trim();
		return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out value)
			|| decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
	}
}
