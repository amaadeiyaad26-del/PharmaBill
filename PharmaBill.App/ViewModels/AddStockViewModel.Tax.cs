using PharmaBill.Core.Inventory;
using PharmaBill.Core.Gst;
using PharmaBill.App.Services;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

// HSN and GST on the Add Stock form. A medicine being created gets its HSN from what it is (dosage form), with the
// reason under the box; the GST box is filled with the last rate this shop recorded for it or its salt ("same as last
// time", dated, never before the September 2025 cut). Both stay editable. With both known, saving asks nothing more.
public sealed partial class AddStockViewModel
{
	private string _hsnText = string.Empty;
	private string _hsnHint = string.Empty;
	private bool _isNewProduct;
	private RememberedRate? _remembered;

	/// <summary>The medicine is not in the master yet: its HSN is entered (or classified) here.</summary>
	public bool IsNewProduct
	{
		get => _isNewProduct;
		private set => SetProperty(ref _isNewProduct, value);
	}

	public string HsnText
	{
		get => _hsnText;
		set
		{
			if (SetProperty(ref _hsnText, value ?? string.Empty))
			{
				OnPropertyChanged(nameof(HsnHint));
				OnExpiryRuleInputsChanged();
			}
		}
	}

	private string? _existingHsn;
	private bool _noExpiryPrinted;

	/// <summary>A durable instrument (HSN chapter 90): no expiry date is needed.</summary>
	public bool IsDurableInstrument => ExpiryRule.IsDurableInstrument(IsNewProduct ? HsnText : _existingHsn);

	/// <summary>The user ticked "No expiry printed on pack": the batch is kept without a date, on record as their choice.</summary>
	public bool NoExpiryPrinted
	{
		get => _noExpiryPrinted;
		set
		{
			if (SetProperty(ref _noExpiryPrinted, value))
			{
				OnExpiryRuleInputsChanged();
			}
		}
	}

	/// <summary>Offer the tick only while the expiry box is empty, and never for an instrument.</summary>
	public bool ShowNoExpiryTick => !IsDurableInstrument && string.IsNullOrWhiteSpace(ExpiryText);

	private void OnExpiryRuleInputsChanged()
	{
		OnPropertyChanged(nameof(IsDurableInstrument));
		OnPropertyChanged(nameof(ShowNoExpiryTick));
	}

	/// <summary>An empty expiry box is accepted only for an instrument or with the tick set.</summary>
	private bool MayHaveNoExpiry => string.IsNullOrWhiteSpace(ExpiryText) && (IsDurableInstrument || NoExpiryPrinted);

	/// <summary>Why the HSN was chosen; cleared once the user types a different code.</summary>
	public string HsnHint => _hsnHint.Length > 0 && _classifiedHsn != null && string.Equals(HsnText.Trim(), _classifiedHsn, StringComparison.OrdinalIgnoreCase) ? _hsnHint : string.Empty;

	private string? _classifiedHsn;

	/// <summary>"Same as last time: 5%, from …, date" while the GST box still shows that rate.</summary>
	public string GstHint => _remembered != null && !_remembered.IsNilRated && GstRateText.TryParse(GstText, out decimal? rate) && rate == _remembered.Rate
		? _remembered.Label + ". Check it against the supplier's invoice."
		: string.Empty;

	private async Task LoadTaxDefaultsAsync(AddStockService service)
	{
		IsNewProduct = await service.WouldCreateProductAsync(DrugId, CatalogMedicineId);
		_existingHsn = IsNewProduct ? null : await service.ExistingHsnAsync(DrugId, CatalogMedicineId);
		OnExpiryRuleInputsChanged();
		if (IsNewProduct && HsnText.Trim().Length == 0 && await service.ClassifyHsnAsync(DrugId, CatalogMedicineId, MedicineName) is HsnSuggestion hsn)
		{
			_classifiedHsn = hsn.Code;
			_hsnHint = hsn.Reason + ". Change it if the invoice shows another code.";
			HsnText = hsn.Code;
		}
		if (GstText.Trim().Length == 0 && await service.LastRecordedRateAsync(DrugId, CatalogMedicineId) is RememberedRate remembered && !remembered.IsNilRated)
		{
			_remembered = remembered;
			GstText = remembered.Rate!.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
		}
		OnPropertyChanged(nameof(GstHint));
	}

	/// <summary>The HSN typed or classified on the form, for a medicine being created.</summary>
	private string? EnteredHsn => IsNewProduct && HsnText.Trim().Length > 0 ? HsnText.Trim() : null;
}
