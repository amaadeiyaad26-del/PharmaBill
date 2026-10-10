using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PrescriptionReviewRow : ObservableObject
{
	private bool _include;

	private decimal _quantity;

	private RetailStockChoice? _choice;

	private string? _matchedName;

	public string PrescribedName { get; }

	public string Dosage { get; }

	public string Duration { get; }

	public double Confidence { get; }

	public RetailStockChoice? Choice => _choice;

	public string? MatchedName => _matchedName;

	public Guid? LinkedDrugId { get; private set; }

	public Guid? LinkedCatalogMedicineId { get; private set; }

	public bool IsMatched => _choice != null;

	public bool IsUnmatched => _choice == null;

	public bool CanInclude => (object)Choice != null;

	public string ConfidenceText
	{
		get
		{
			if ((object)Choice != null)
			{
				return $"{Confidence:P0}";
			}
			return "-";
		}
	}

	public string DoseText => string.Join("  ", new string[2] { Dosage, Duration }.Where((string part) => part.Length > 0));

	public string MatchText => _matchedName ?? "Not matched - link or substitute manually";

	public string QuantityText
	{
		get
		{
			return _quantity.ToString("0.##", CultureInfo.CurrentCulture);
		}
		set
		{
			string text = value?.Trim() ?? string.Empty;
			if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal parsed)
				|| decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed))
			{
				Quantity = parsed < 0m ? 0m : parsed;
			}
		}
	}

	public string BatchText
	{
		get
		{
			if ((object)Choice != null)
			{
				return $"{Choice.BatchNo}  exp {(Choice.ExpiryDate.HasValue ? Choice.ExpiryDate.Value.ToString("MM/yyyy") : "-")}  stock {Choice.AvailableQuantity:0.##}";
			}
			if (MatchedName != null)
			{
				return "Out of stock";
			}
			return "Not in catalogue";
		}
	}

	public bool IsValid
	{
		get
		{
			if ((object)Choice != null && Quantity > 0m)
			{
				return Quantity <= Choice.AvailableQuantity;
			}
			return false;
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public bool Include
	{
		get
		{
			return _include;
		}
		set
		{
			if (!EqualityComparer<bool>.Default.Equals(_include, value))
			{
				OnPropertyChanging(nameof(Include));
				_include = value;
				OnPropertyChanged(nameof(Include));
			}
		}
	}

	[GeneratedCode("CommunityToolkit.Mvvm.SourceGenerators.ObservablePropertyGenerator", "8.4.0.0")]
	[ExcludeFromCodeCoverage]
	public decimal Quantity
	{
		get
		{
			return _quantity;
		}
		set
		{
			if (!EqualityComparer<decimal>.Default.Equals(_quantity, value))
			{
				OnPropertyChanging(nameof(Quantity));
				_quantity = value;
				OnPropertyChanged(nameof(Quantity));
			}
		}
	}

	public PrescriptionReviewRow(PrescribedItemDto item, RetailStockChoice? choice, string? matchedName)
	{
		PrescribedName = item.DrugName;
		Dosage = item.Dosage;
		Duration = item.Duration;
		Confidence = item.ConfidenceScore;
		_choice = choice;
		_matchedName = choice?.DrugName ?? matchedName;
		LinkedDrugId = choice?.DrugId;
		decimal val = PrescriptionTextParser.EstimateQuantity(item.Dosage, item.Duration) ?? 1m;
		_quantity = ((object)choice == null) ? Math.Max(val, 1m) : Math.Min(Math.Max(val, 1m), choice.AvailableQuantity);
		_include = (object)choice != null;
	}

	public void ApplyStock(RetailStockChoice choice)
	{
		_choice = choice;
		_matchedName = choice.DrugName;
		LinkedDrugId = choice.DrugId;
		if (_quantity <= 0m)
		{
			_quantity = 1m;
		}

		if (_quantity > choice.AvailableQuantity)
		{
			_quantity = choice.AvailableQuantity;
		}

		_include = true;
		RaiseMatchChanged();
	}

	public void ApplyLink(Guid drugId, Guid? catalogMedicineId, string name)
	{
		LinkedDrugId = drugId;
		LinkedCatalogMedicineId = catalogMedicineId;
		_matchedName = name;
		_choice = null;
		RaiseMatchChanged();
	}

	private void RaiseMatchChanged()
	{
		OnPropertyChanged(nameof(Choice));
		OnPropertyChanged(nameof(MatchedName));
		OnPropertyChanged(nameof(MatchText));
		OnPropertyChanged(nameof(BatchText));
		OnPropertyChanged(nameof(ConfidenceText));
		OnPropertyChanged(nameof(CanInclude));
		OnPropertyChanged(nameof(IsMatched));
		OnPropertyChanged(nameof(IsUnmatched));
		OnPropertyChanged(nameof(Include));
		OnPropertyChanged(nameof(Quantity));
		OnPropertyChanged(nameof(QuantityText));
		OnPropertyChanged(nameof(IsValid));
	}
}
