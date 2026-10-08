using System;
using System.Globalization;
using System.Linq;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;

namespace PharmaBill.App.ViewModels;

public sealed class PurchaseMedicinePickerItem
{
	private PurchaseMedicinePickerItem(
		string kind,
		string displayTitle,
		string displayDetail,
		string searchText,
		Drug? drug,
		Guid? catalogMedicineId,
		string? composition,
		string? manufacturer,
		string? dosageForm,
		string? strength,
		decimal? suggestedGst,
		string? suggestedHsn,
		decimal? suggestedMrp,
		string? packSizeLabel,
		decimal? suggestedPurchaseRate,
		bool isAddManually)
	{
		Kind = kind;
		DisplayTitle = displayTitle;
		DisplayDetail = displayDetail;
		SearchText = searchText;
		Drug = drug;
		CatalogMedicineId = catalogMedicineId;
		Composition = composition;
		Manufacturer = manufacturer;
		DosageForm = dosageForm;
		Strength = strength;
		SuggestedGst = suggestedGst;
		SuggestedHsn = suggestedHsn;
		SuggestedMrp = suggestedMrp;
		PackSizeLabel = packSizeLabel;
		SuggestedPurchaseRate = suggestedPurchaseRate;
		IsAddManually = isAddManually;
	}

	public string Kind { get; }

	public string DisplayTitle { get; }

	public string DisplayDetail { get; }

	public string SearchText { get; }

	public Drug? Drug { get; }

	public Guid? CatalogMedicineId { get; }

	public string? Composition { get; }

	public string? Manufacturer { get; }

	public string? DosageForm { get; }

	public string? Strength { get; }

	public decimal? SuggestedGst { get; }

	public string? SuggestedHsn { get; }

	public decimal? SuggestedMrp { get; }

	public string? PackSizeLabel { get; }

	public decimal? SuggestedPurchaseRate { get; }

	public bool IsAddManually { get; }

	public bool IsLocalDrug => Drug != null && !IsAddManually;

	public bool IsCatalogue => CatalogMedicineId.HasValue && !IsAddManually;

	public static PurchaseMedicinePickerItem FromLocalDrug(Drug drug)
	{
		string form = string.IsNullOrWhiteSpace(drug.DosageForm) ? "" : drug.DosageForm.Trim();
		string strength = string.IsNullOrWhiteSpace(drug.Strength) ? "" : drug.Strength.Trim();
		string gst = drug.GstRate is > 0m ? $"GST {drug.GstRate:0.##}%" : "GST —";
		string mrp = drug.Mrp is > 0m ? $"MRP ₹{drug.Mrp.Value.ToString("0.##", CultureInfo.CurrentCulture)}" : null;
		string detail = string.Join(" · ", new[] { form, strength, mrp, gst, string.IsNullOrWhiteSpace(drug.HsnCode) ? null : $"HSN {drug.HsnCode}" }.Where(static s => !string.IsNullOrWhiteSpace(s)));
		return new PurchaseMedicinePickerItem(
			"Local",
			drug.Name,
			string.IsNullOrWhiteSpace(detail) ? "In medicine master" : detail!,
			drug.Name,
			drug,
			drug.CatalogMedicineId,
			drug.GenericName,
			null,
			drug.DosageForm,
			drug.Strength,
			drug.GstRate,
			drug.HsnCode,
			drug.Mrp,
			packSizeLabel: string.IsNullOrWhiteSpace(drug.Unit) ? drug.DosageForm : drug.Unit,
			suggestedPurchaseRate: null,
			isAddManually: false);
	}

	public static PurchaseMedicinePickerItem FromCatalogue(MedicineSearchResult result, decimal? gstHint, string? hsnHint)
	{
		decimal? mrp = result.Mrp is > 0m ? result.Mrp : result.ReferencePrice;
		string form = string.IsNullOrWhiteSpace(result.PackSize) ? "" : result.PackSize!;
		string gst = gstHint is > 0m ? $"GST {gstHint:0.##}%" : "GST (set on add)";
		string mrpText = mrp is > 0m ? $"MRP ₹{mrp.Value.ToString("0.##", CultureInfo.CurrentCulture)}" : null;
		string detail = string.Join(" · ", new[]
		{
			string.IsNullOrWhiteSpace(result.Composition) ? null : result.Composition,
			form,
			mrpText,
			gst,
			string.IsNullOrWhiteSpace(result.Manufacturer) ? null : result.Manufacturer
		}.Where(static s => !string.IsNullOrWhiteSpace(s)));
		return new PurchaseMedicinePickerItem(
			"Catalogue",
			result.Name,
			string.IsNullOrWhiteSpace(detail) ? "Drug Bank catalogue" : detail!,
			result.Name,
			drug: null,
			result.CatalogMedicineId,
			result.Composition,
			result.Manufacturer,
			dosageForm: null,
			strength: null,
			gstHint,
			hsnHint,
			mrp,
			result.PackSize,
			suggestedPurchaseRate: null,
			isAddManually: false);
	}

	public static PurchaseMedicinePickerItem AddManually(string typedName)
	{
		string name = typedName.Trim();
		return new PurchaseMedicinePickerItem(
			"Manual",
			$"+ Add '{name}' Manually",
			"Create a new medicine in Medicine Master with formulation, HSN and GST",
			name,
			drug: null,
			catalogMedicineId: null,
			composition: null,
			manufacturer: null,
			dosageForm: null,
			strength: null,
			suggestedGst: 12m,
			suggestedHsn: null,
			suggestedMrp: null,
			packSizeLabel: null,
			suggestedPurchaseRate: null,
			isAddManually: true);
	}
}
