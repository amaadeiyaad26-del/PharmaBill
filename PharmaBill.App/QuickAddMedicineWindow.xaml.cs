using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.App.Services;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;

namespace PharmaBill.App;

public partial class QuickAddMedicineWindow : Window
{
	private readonly IServiceScopeFactory _scopes;
	private readonly Guid? _userId;

	public QuickAddMedicineWindow(IServiceScopeFactory scopes, Guid? userId, string? brandName, SmartDrugSuggestion? suggestion = null, string? notice = null, string? barcode = null)
	{
		_scopes = scopes;
		_userId = userId;
		InitializeComponent();
		BrandBox.Text = brandName?.Trim() ?? string.Empty;
		BarcodeBox.Text = barcode?.Trim() ?? string.Empty;
		ApplySuggestion(suggestion, notice);
		Loaded += (_, _) =>
		{
			if (string.IsNullOrWhiteSpace(BrandBox.Text))
			{
				BrandBox.Focus();
			}
			else if (suggestion != null)
			{
				RateBox.Focus();
			}
			else
			{
				GenericBox.Focus();
			}
		};
	}

	private void ApplySuggestion(SmartDrugSuggestion? suggestion, string? notice)
	{
		if (!string.IsNullOrWhiteSpace(notice))
		{
			NoticeText.Text = notice;
			NoticeText.Visibility = Visibility.Visible;
		}

		if (suggestion == null)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(suggestion.BrandName))
		{
			BrandBox.Text = suggestion.BrandName.Trim();
		}

		FillFromSuggestion(suggestion);
	}

	private async void SearchOnline_Click(object sender, RoutedEventArgs e)
	{
		string brand = BrandBox.Text.Trim();
		if (brand.Length < 2)
		{
			ShowNotice("Type at least 2 letters in the brand name, then search online.");
			BrandBox.Focus();
			return;
		}

		SearchOnlineButton.IsEnabled = false;
		SearchOnlineButton.Content = "Searching...";
		StatusText.Text = string.Empty;
		try
		{
			using IServiceScope scope = _scopes.CreateScope();
			ISmartDrugLookupService lookup = scope.ServiceProvider.GetRequiredService<ISmartDrugLookupService>();
			SmartDrugLookupOutcome outcome = await lookup.LookupAsync(brand);
			ShowNotice(outcome.Message);
			if (outcome.Suggestion != null)
			{
				FillFromSuggestion(outcome.Suggestion);
				BrandBox.Text = brand;
			}
		}
		catch (Exception ex)
		{
			ShowNotice(ex.Message);
		}
		finally
		{
			SearchOnlineButton.IsEnabled = true;
			SearchOnlineButton.Content = "🔍 Search Online / AI";
		}
	}

	private void FillFromSuggestion(SmartDrugSuggestion suggestion)
	{
		if (!string.IsNullOrWhiteSpace(suggestion.GenericName))
		{
			GenericBox.Text = suggestion.GenericName.Trim();
		}

		if (!string.IsNullOrWhiteSpace(suggestion.Manufacturer))
		{
			ManufacturerBox.Text = suggestion.Manufacturer.Trim();
		}

		if (!string.IsNullOrWhiteSpace(suggestion.PackSizeLabel))
		{
			PackBox.Text = suggestion.PackSizeLabel.Trim();
		}

		HsnBox.Text = string.IsNullOrWhiteSpace(suggestion.HsnCode) ? "3004" : suggestion.HsnCode.Trim();
		GstBox.Text = suggestion.GstRate.ToString("0.##", CultureInfo.InvariantCulture);
		if (suggestion.ApproximateMrp is > 0m)
		{
			MrpBox.Text = suggestion.ApproximateMrp.Value.ToString("0.##", CultureInfo.InvariantCulture);
		}
	}

	private void ShowNotice(string message)
	{
		NoticeText.Text = message;
		NoticeText.Visibility = Visibility.Visible;
	}

	public CustomMedicineResult? Result { get; private set; }

	private async void Save_Click(object sender, RoutedEventArgs e)
	{
		StatusText.Text = string.Empty;
		string brand = BrandBox.Text.Trim();
		if (brand.Length == 0)
		{
			StatusText.Text = "Brand name is required.";
			BrandBox.Focus();
			return;
		}

		if (!TryOptionalDecimal(GstBox.Text, out decimal gst))
		{
			StatusText.Text = "GST % must be a number from 0 to 100.";
			GstBox.Focus();
			return;
		}

		if (string.IsNullOrWhiteSpace(GstBox.Text))
		{
			gst = 12m;
		}

		if (gst < 0m || gst > 100m)
		{
			StatusText.Text = "GST % must be a number from 0 to 100.";
			return;
		}

		if (!TryOptionalDecimal(MrpBox.Text, out decimal mrp))
		{
			StatusText.Text = "MRP must be a number.";
			return;
		}

		if (!TryOptionalDecimal(RateBox.Text, out decimal rate))
		{
			StatusText.Text = "Purchase rate must be a number.";
			return;
		}

		SaveButton.IsEnabled = false;
		try
		{
			using IServiceScope scope = _scopes.CreateScope();
			Result = await scope.ServiceProvider.GetRequiredService<CustomMedicineService>().SaveAsync(
				new CustomMedicineRequest(
					brand,
					GenericBox.Text,
					ManufacturerBox.Text,
					PackBox.Text,
					HsnBox.Text,
					gst,
					string.IsNullOrWhiteSpace(MrpBox.Text) ? null : mrp,
					string.IsNullOrWhiteSpace(RateBox.Text) ? null : rate,
					BarcodeBox.Text),
				_userId);
			DialogResult = true;
		}
		catch (Exception ex)
		{
			StatusText.Text = ex.Message;
			SaveButton.IsEnabled = true;
		}
	}

	private static bool TryOptionalDecimal(string text, out decimal value)
	{
		value = 0m;
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		return decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out value)
			|| decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
	}
}
