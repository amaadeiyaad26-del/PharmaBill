using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Core.Entities;

namespace PharmaBill.App.ViewModels;

/// <summary>One Gemini-parsed purchase row waiting for the chemist to apply it to the bill.</summary>
public sealed class StagedPurchaseLine : ObservableObject
{
	private string _itemName = string.Empty;

	private string _batch = string.Empty;

	private string _expiry = string.Empty;

	private decimal _quantity;

	private decimal _free;

	private decimal _mrp;

	private decimal _rate;

	private decimal _gst;

	private decimal _discountPercent;

	private string? _validationWarning;

	public Drug? Drug { get; set; }

	public string ItemName
	{
		get => _itemName;
		set => SetProperty(ref _itemName, value ?? string.Empty);
	}

	public string Batch
	{
		get => _batch;
		set => SetProperty(ref _batch, value ?? string.Empty);
	}

	public string Expiry
	{
		get => _expiry;
		set => SetProperty(ref _expiry, value ?? string.Empty);
	}

	public decimal Quantity
	{
		get => _quantity;
		set => SetProperty(ref _quantity, value);
	}

	public decimal Free
	{
		get => _free;
		set => SetProperty(ref _free, value);
	}

	public decimal Mrp
	{
		get => _mrp;
		set => SetProperty(ref _mrp, value);
	}

	public decimal Rate
	{
		get => _rate;
		set => SetProperty(ref _rate, value);
	}

	public decimal Gst
	{
		get => _gst;
		set => SetProperty(ref _gst, value);
	}

	public decimal DiscountPercent
	{
		get => _discountPercent;
		set => SetProperty(ref _discountPercent, value);
	}

	public string? ValidationWarning
	{
		get => _validationWarning;
		set => SetProperty(ref _validationWarning, value);
	}

	public string MatchText => Drug == null ? "Not in medicine master" : "Matched";

	public static string FormatAmount(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
