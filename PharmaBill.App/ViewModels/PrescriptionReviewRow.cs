using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PrescriptionReviewRow : ObservableObject
{
	private bool _include;

	private decimal _quantity;

	public string PrescribedName { get; }

	public string Dosage { get; }

	public string Duration { get; }

	public double Confidence { get; }

	public RetailStockChoice? Choice { get; }

	public string? MatchedName { get; }

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

	public string MatchText => MatchedName ?? "Not matched - link or substitute manually";

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
		Choice = choice;
		MatchedName = choice?.DrugName ?? matchedName;
		decimal val = PrescriptionTextParser.EstimateQuantity(item.Dosage, item.Duration) ?? 1m;
		_quantity = (((object)choice == null) ? 0m : Math.Min(Math.Max(val, 1m), choice.AvailableQuantity));
		_include = (object)choice != null;
	}
}
