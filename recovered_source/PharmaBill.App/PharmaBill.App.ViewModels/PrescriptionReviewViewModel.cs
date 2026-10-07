using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using PharmaBill.Core.Ai;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PrescriptionReviewViewModel : ObservableObject
{
	public string Summary { get; }

	public bool ApiKeyMissing { get; }

	public bool OpenSettingsRequested { get; set; }

	public ObservableCollection<PrescriptionReviewRow> Rows { get; } = new ObservableCollection<PrescriptionReviewRow>();

	public PrescriptionReviewViewModel(PrescriptionParseResultDto result, IReadOnlyDictionary<Guid, RetailStockChoice> fefoChoices, IReadOnlyDictionary<Guid, string> matchedNames)
	{
		ApiKeyMissing = result.ApiKeyMissing;
		Summary = $"{result.Source}: {result.DetectedMedicines.Count} medicine(s) detected. {result.Message}".Trim();
		foreach (PrescribedItemDto detectedMedicine in result.DetectedMedicines)
		{
			RetailStockChoice value = null;
			string value2 = null;
			Guid? matchedCatalogProductId = detectedMedicine.MatchedCatalogProductId;
			if (matchedCatalogProductId.HasValue)
			{
				Guid valueOrDefault = matchedCatalogProductId.GetValueOrDefault();
				fefoChoices.TryGetValue(valueOrDefault, out value);
				matchedNames.TryGetValue(valueOrDefault, out value2);
			}
			Rows.Add(new PrescriptionReviewRow(detectedMedicine, value, value2));
		}
	}

	public string? Validate()
	{
		foreach (PrescriptionReviewRow item in Rows.Where((PrescriptionReviewRow row) => row.Include))
		{
			if (!item.IsValid)
			{
				return "Check the quantity for " + item.MatchText + ": it must be above zero and within stock.";
			}
		}
		return null;
	}

	public IReadOnlyList<ConfirmedPrescriptionItem> GetConfirmedItems()
	{
		return (from row in Rows
			where row.Include && row.IsValid
			select new ConfirmedPrescriptionItem(row.Choice, row.Quantity)).ToList();
	}
}
