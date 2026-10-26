using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.App.Services;
using PharmaBill.Core;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>
/// What a counter command will add to the bill, shown before it is added: the medicine, quantity, customer, the batch
/// (earliest expiry first, changeable), and the tax the invoice will carry for the line.
/// </summary>
public sealed class PosCommandPreview : ObservableObject
{
	private RetailStockChoice _selectedBatch;

	public PosCommandPreview(PosStockMatch match, decimal packQuantity, string quantityText, string? customer, IReadOnlyList<string> notes)
	{
		Match = match;
		PackQuantity = packQuantity;
		QuantityText = quantityText;
		Customer = customer;
		Notes = notes;
		_selectedBatch = match.Batches[0];
	}

	public PosStockMatch Match { get; }

	public string DrugName => Match.DrugName;

	/// <summary>Quantity in packs, as stock is counted.</summary>
	public decimal PackQuantity { get; }

	public string QuantityText { get; }

	public string? Customer { get; }

	public bool HasCustomer => !string.IsNullOrWhiteSpace(Customer);

	/// <summary>Things the pharmacist should know before adding: a corrected spelling, a converted quantity.</summary>
	public IReadOnlyList<string> Notes { get; }

	public bool HasNotes => Notes.Count > 0;

	public IReadOnlyList<RetailStockChoice> Batches => Match.Batches;

	/// <summary>More than one batch: the dropdown is shown so the batch dispensed is a choice, as expiry tracking requires.</summary>
	public bool HasBatchChoice => Batches.Count > 1;

	public RetailStockChoice SelectedBatch
	{
		get => _selectedBatch;
		set
		{
			if (value != null && SetProperty(ref _selectedBatch, value))
			{
				OnPropertyChanged(nameof(BatchSummary));
				OnPropertyChanged(nameof(EnoughStock));
				OnPropertyChanged(nameof(StockWarning));
				OnPropertyChanged(nameof(TaxSummary));
				OnPropertyChanged(nameof(AmountText));
			}
		}
	}

	public string BatchSummary => Describe(SelectedBatch);

	public bool EnoughStock => PackQuantity <= SelectedBatch.AvailableQuantity;

	public string StockWarning => EnoughStock ? string.Empty : $"Only {SelectedBatch.AvailableQuantity:0.##} in batch {SelectedBatch.BatchNo}. Choose another batch or lower the quantity.";

	public decimal UnitPrice => SelectedBatch.SalePrice is >= 0m and var sale && sale <= SelectedBatch.Mrp ? sale : SelectedBatch.Mrp;

	public string AmountText => MoneyFormat.Rupees(decimal.Round(PackQuantity * UnitPrice, 2, MidpointRounding.AwayFromZero));

	/// <summary>The invoice line's HSN and tax split (CGST and SGST halves, as the invoice prints them).</summary>
	public string TaxSummary
	{
		get
		{
			ProductTaxDetails? tax = SelectedBatch.KnownTax;
			if (tax == null)
			{
				return "HSN / GST not on record: the bill asks for them before it is saved.";
			}
			InclusiveTaxLine split = RetailTaxCalculator.SplitInclusive(PackQuantity, UnitPrice, tax.GstRate, 0m);
			decimal cgst = decimal.Round(split.TaxAmount / 2m, 2, MidpointRounding.AwayFromZero);
			decimal sgst = split.TaxAmount - cgst;
			string rate = tax.IsNilRated ? "nil-rated" : tax.GstRate.ToString("0.##", CultureInfo.InvariantCulture) + "% GST";
			return $"HSN {tax.HsnCode} · {rate} · taxable {MoneyFormat.Rupees(split.NetAmount)} · CGST {MoneyFormat.Rupees(cgst)} · SGST {MoneyFormat.Rupees(sgst)}";
		}
	}

	public static string Describe(RetailStockChoice batch) =>
		$"Batch {batch.BatchNo} · exp {(batch.ExpiryDate.HasValue ? batch.ExpiryDate.Value.ToString("MM/yy", CultureInfo.InvariantCulture) : "—")} · {batch.AvailableQuantity:0.##} in stock · MRP {MoneyFormat.Rupees(batch.Mrp)}";
}
