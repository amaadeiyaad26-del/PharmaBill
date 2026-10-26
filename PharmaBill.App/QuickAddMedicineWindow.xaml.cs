using System.Globalization;
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
		PreviewKeyDown += OnArrowKeyNavigate;
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

	// Up and Down move between the fields, like Enter moves forward. Only when a text box has focus.
	private void OnArrowKeyNavigate(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key is not (System.Windows.Input.Key.Up or System.Windows.Input.Key.Down) || System.Windows.Input.Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
		{
			return;
		}

		e.Handled = true;
		if (e.Key == System.Windows.Input.Key.Down)
		{
			Behaviors.KeyboardFocusManager.MoveNext();
		}
		else
		{
			Behaviors.KeyboardFocusManager.MovePrevious();
		}
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

	private async void SearchDataBank_Click(object sender, RoutedEventArgs e)
	{
		string brand = BrandBox.Text.Trim();
		if (brand.Length < 2)
		{
			ShowNotice("Type at least 2 letters in the brand name, then search the data bank.");
			BrandBox.Focus();
			return;
		}

		SearchDataBankButton.IsEnabled = false;
		SearchDataBankButton.Content = "Searching...";
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
			ShowNotice(PharmaBill.Core.UserMessage.Of(ex));
		}
		finally
		{
			SearchDataBankButton.IsEnabled = true;
			SearchDataBankButton.Content = SmartDrugLookupPrompt.ButtonLabel;
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

		// Tax is filled only when this pharmacy has it recorded on a product; it is never guessed from the name.
		if (!string.IsNullOrWhiteSpace(suggestion.HsnCode))
		{
			HsnBox.Text = suggestion.HsnCode.Trim();
		}

		if (suggestion.IsNilRated)
		{
			NilRatedBox.IsChecked = true;
		}
		else if (suggestion.GstRate is > 0m)
		{
			GstBox.Text = suggestion.GstRate.Value.ToString("0.##", CultureInfo.InvariantCulture);
		}
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

		// HSN and GST are required. A blank rate is never defaulted; 0% only through Nil-rated.
		bool nilRated = NilRatedBox.IsChecked == true;
		if (!GstRateText.TryParse(GstBox.Text, out decimal? gst))
		{
			StatusText.Text = "GST % must be a number, for example 5 or 12.";
			GstBox.Focus();
			return;
		}

		if (!PharmaBill.Core.Gst.ProductTax.TryCreate(HsnBox.Text, gst, nilRated, out _, out string? taxError))
		{
			StatusText.Text = taxError ?? string.Empty;
			(string.IsNullOrWhiteSpace(HsnBox.Text) ? HsnBox : GstBox).Focus();
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
					nilRated ? null : gst,
					string.IsNullOrWhiteSpace(MrpBox.Text) ? null : mrp,
					string.IsNullOrWhiteSpace(RateBox.Text) ? null : rate,
					BarcodeBox.Text,
					nilRated),
				_userId);
			DialogResult = true;
		}
		catch (Exception ex)
		{
			StatusText.Text = PharmaBill.Core.UserMessage.Of(ex);
			SaveButton.IsEnabled = true;
		}
	}

	private void NilRated_Changed(object sender, RoutedEventArgs e)
	{
		bool nil = NilRatedBox.IsChecked == true;
		GstBox.IsEnabled = !nil;
		if (nil)
		{
			GstBox.Text = string.Empty;
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
