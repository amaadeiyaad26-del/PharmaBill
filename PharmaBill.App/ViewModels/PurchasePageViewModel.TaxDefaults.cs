using PharmaBill.Core.Gst;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

// HSN code and GST % on the Add item row. Picking a medicine fills them from its master record; a medicine with neither
// gets its HSN from its dosage form and its GST % from the last rate recorded for it or its salt (dated, never before the
// September 2025 cut). The pharmacist can overtype either box for this invoice; the note under the row says where the
// values came from.
public sealed partial class PurchasePageViewModel
{
	private bool _fillingTax;
	private string _filledHsn = string.Empty;
	private string _filledGst = string.Empty;
	private bool _taxFromMaster;
	private string _taxSourceHint = string.Empty;

	/// <summary>Where the row's HSN and GST came from, or what to do when nothing is on record.</summary>
	public string TaxSourceHint
	{
		get => _taxSourceHint;
		private set
		{
			if (SetProperty(ref _taxSourceHint, value))
			{
				OnPropertyChanged(nameof(HasTaxSourceHint));
			}
		}
	}

	public bool HasTaxSourceHint => TaxSourceHint.Length > 0;

	private bool _isMedicineListReady;

	/// <summary>The medicine master has finished loading in the background (or failed to, with an error shown).</summary>
	public bool IsMedicineListReady
	{
		get => _isMedicineListReady;
		private set => SetProperty(ref _isMedicineListReady, value);
	}

	/// <summary>Sets the row's tax boxes as the screen's own suggestion, not the pharmacist's entry.</summary>
	private void FillTax(Action fill)
	{
		_fillingTax = true;
		try
		{
			fill();
		}
		finally
		{
			_fillingTax = false;
		}
		_filledHsn = NewItemHsn;
		_filledGst = NewItemGst;
	}

	// A new medicine never keeps the last one's tax: its boxes start empty and fill from its own record.
	private void ResetTaxForNewMedicine()
	{
		FillTax(() =>
		{
			NewItemHsn = string.Empty;
			NewItemGst = string.Empty;
		});
		TaxSourceHint = string.Empty;
	}

	private bool TaxComplete => NewItemHsn.Trim().Length > 0 && (NewItemNilRated || NewItemGst.Trim().Length > 0);

	private void DescribeMasterTax()
	{
		if (TaxComplete)
		{
			_taxFromMaster = true;
			TaxSourceHint = $"HSN {NewItemHsn} · GST {(NewItemNilRated ? "nil" : NewItemGst + "%")} from the medicine master. Edit either box if this invoice differs.";
		}
	}

	/// <summary>
	/// Nothing on the medicine's record: the HSN comes from what it is (dosage form), the GST % from the last rate this
	/// shop recorded for it or its salt ("same as last time", dated, never before the September 2025 cut). The note under
	/// the row says where each came from; both boxes stay editable.
	/// </summary>
	private async Task FillTaxDefaultsAsync(Guid? drugId, Guid? catalogMedicineId, string? name, PharmaBillDbContext context, CancellationToken cancellationToken)
	{
		if (TaxComplete)
		{
			DescribeMasterTax();
			return;
		}
		var taxes = new ProductTaxService(context);
		HsnSuggestion? hsn = NewItemHsn.Trim().Length == 0 ? await taxes.ClassifyHsnAsync(drugId, catalogMedicineId, name, cancellationToken) : null;
		RememberedRate? remembered = NewItemGst.Trim().Length == 0 && !NewItemNilRated ? await taxes.LastRecordedRateAsync(drugId, catalogMedicineId, cancellationToken) : null;
		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}
		FillTax(() =>
		{
			if (hsn != null)
			{
				NewItemHsn = hsn.Code;
			}
			if (remembered != null)
			{
				if (remembered.IsNilRated)
				{
					NewItemNilRated = true;
				}
				else
				{
					NewItemGst = FormatAmount(remembered.Rate!.Value);
				}
			}
		});
		_taxFromMaster = false;
		var notes = new List<string>();
		if (hsn != null)
		{
			notes.Add(hsn.Reason);
		}
		if (remembered != null)
		{
			notes.Add(remembered.Label);
		}
		TaxSourceHint = notes.Count > 0
			? string.Join(". ", notes) + ". Check them against the supplier's invoice; what you enter is saved to this medicine."
			: "No HSN or GST on record for this medicine. Enter them from the supplier's invoice; they are saved to the medicine.";
	}

	// The pharmacist overtyped a value the screen filled in: say what it replaces and what it changes.
	private void OnTaxBoxEdited()
	{
		if (_fillingTax || (_filledHsn.Length == 0 && _filledGst.Length == 0))
		{
			return;
		}
		if (!string.Equals(NewItemHsn.Trim(), _filledHsn.Trim(), StringComparison.OrdinalIgnoreCase) || NewItemGst.Trim() != _filledGst.Trim())
		{
			TaxSourceHint = _taxFromMaster
				? $"Changed from the master's HSN {Shown(_filledHsn)} · GST {Shown(_filledGst)}%. A different HSN also corrects the medicine master (audited); the GST % applies to this invoice line."
				: $"Changed from the suggested HSN {Shown(_filledHsn)} · GST {Shown(_filledGst)}%. What you enter is saved to this medicine.";
		}
	}

	// The last purchase of this medicine was billed at another GST %: that rate is offered, and the note says so.
	private void NoteGstFromLastPurchase(string invoiceNo, DateOnly invoiceDate)
	{
		TaxSourceHint = $"HSN {NewItemHsn} {(_taxFromMaster ? "from the medicine master" : "as suggested")}; GST {NewItemGst}% same as last time, from supplier invoice {invoiceNo}, {invoiceDate:dd-MMM-yyyy}. Edit either box if this invoice differs.";
	}

	private static string Shown(string value) => value.Trim().Length == 0 ? "—" : value.Trim();

	private void ClearTaxSource()
	{
		_filledHsn = string.Empty;
		_filledGst = string.Empty;
		_taxFromMaster = false;
		TaxSourceHint = string.Empty;
	}
}
