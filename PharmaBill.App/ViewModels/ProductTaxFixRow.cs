using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.App.Services;
using PharmaBill.Core.Gst;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

/// <summary>One product in "Products missing HSN or GST rate", editable in place so a shop can fix many at once.</summary>
public sealed class ProductTaxFixRow : ObservableObject
{
	private string _hsnCode;

	private string _gstRateText;

	private bool _isNilRated;

	private string _rowError = string.Empty;

	public ProductTaxFixRow(ProductMissingTaxRow row)
	{
		DrugId = row.DrugId;
		Name = row.Name;
		Missing = row.Missing;
		_hsnCode = row.HsnCode?.Trim() ?? string.Empty;
		// A stored 0% that nobody marked Nil-rated is "not known", so it starts blank rather than as 0.
		_gstRateText = row.GstRate is > 0m ? row.GstRate.Value.ToString("0.##") : string.Empty;
		_isNilRated = row.IsNilRated;
		OriginalHsn = _hsnCode;
		OriginalRateText = _gstRateText;
	}

	public Guid DrugId { get; }

	public string Name { get; }

	public string Missing { get; }

	private string OriginalHsn { get; }

	private string OriginalRateText { get; }

	public string HsnCode
	{
		get => _hsnCode;
		set => SetProperty(ref _hsnCode, value ?? string.Empty);
	}

	public string GstRateText
	{
		get => _gstRateText;
		set => SetProperty(ref _gstRateText, value ?? string.Empty);
	}

	public bool IsNilRated
	{
		get => _isNilRated;
		set
		{
			if (SetProperty(ref _isNilRated, value) && value)
			{
				GstRateText = string.Empty;
			}
		}
	}

	public string RowError
	{
		get => _rowError;
		set => SetProperty(ref _rowError, value ?? string.Empty);
	}

	/// <summary>True once the user has typed or ticked something on this row.</summary>
	public bool IsEdited => IsNilRated || !string.Equals(HsnCode.Trim(), OriginalHsn, StringComparison.Ordinal)
		|| !string.Equals(GstRateText.Trim(), OriginalRateText, StringComparison.Ordinal);

	/// <summary>The validated update, or null with <see cref="RowError"/> set.</summary>
	public ProductTaxUpdate? TryBuildUpdate()
	{
		if (!PharmaBill.App.Services.GstRateText.TryParse(GstRateText, out decimal? rate))
		{
			RowError = "GST rate must be a number.";
			return null;
		}

		if (!ProductTax.TryCreate(HsnCode, IsNilRated ? null : rate, IsNilRated, out ProductTaxDetails? details, out string? error))
		{
			RowError = error ?? string.Empty;
			return null;
		}

		RowError = string.Empty;
		return new ProductTaxUpdate(DrugId, details!.HsnCode, details.IsNilRated ? null : details.GstRate, details.IsNilRated);
	}
}
