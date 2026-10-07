using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Ai;
using PharmaBill.Core.Catalog;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class LocalPharmacyAssistantService(PharmaBillDbContext context, AiAssistantOptions options) : IAiPharmacyAssistantService
{
	public const int LookbackDays = 30;

	public const int CoverDays = 30;

	public const int NearExpiryDays = 60;

	private const int MaxCandidates = 500;

	private static readonly HashSet<string> Units = new HashSet<string>(StringComparer.Ordinal) { "mg", "mcg", "g", "ml", "iu", "w", "v" };

	private const double AvailabilityWeight = 50.0;

	private const double EconomyWeight = 30.0;

	private const double MarginWeight = 20.0;

	public async Task<List<MedicineSubstituteDto>> FindSubstitutesAsync(string saltComposition, int targetQuantity)
	{
		string[] compositions = (saltComposition ?? string.Empty).Split(new char[4] { '+', '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		string key = CompositionKeyBuilder.Build(compositions);
		if (key.Length == 0)
		{
			return new List<MedicineSubstituteDto>();
		}
		HashSet<string> requestedSalts = SaltNames(key);
		string prefilter = requestedSalts.First();
		List<CatalogMedicine> candidates = (await (from medicine in context.CatalogMedicines.AsNoTracking()
			where !medicine.IsDiscontinued && medicine.CompositionKey.Contains(prefilter)
			select medicine).Take(500).ToListAsync()).Where((CatalogMedicine medicine) => medicine.CompositionKey.Length > 0 && SaltNames(medicine.CompositionKey).SetEquals(requestedSalts)).ToList();
		if (candidates.Count == 0)
		{
			return new List<MedicineSubstituteDto>();
		}
		Guid[] catalogIds = candidates.Select((CatalogMedicine item) => item.Id).ToArray();
		List<Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive && drug.CatalogMedicineId.HasValue && catalogIds.Contains(drug.CatalogMedicineId.Value)
			select drug).ToListAsync();
		Guid[] drugIds = drugs.Select((Drug drug) => drug.Id).ToArray();
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<Batch> batches = await (from batch in context.Batches.AsNoTracking()
			where drugIds.Contains(batch.DrugId) && batch.Quantity > 0m && (batch.ExpiryDate == null || batch.ExpiryDate >= today)
			select batch).ToListAsync();
		string[] names = candidates.Select((CatalogMedicine item) => item.Name.ToLowerInvariant()).Distinct().ToArray();
		HashSet<string> habits = await (from info in context.CatalogInfos.AsNoTracking()
			where info.IsHabitForming && names.Contains(info.NameKey)
			select info.NameKey).ToHashSetAsync();
		List<(CatalogMedicine Medicine, Drug Drug, decimal Quantity, DateOnly? Expiry, decimal? Price, decimal? Margin)> source = candidates.Select((CatalogMedicine medicine) =>
		{
			List<Drug> drugForMedicine = drugs.Where((Drug drug2) => drug2.CatalogMedicineId == medicine.Id).ToList();
			List<Batch> source2 = batches.Where((Batch batch) => drugForMedicine.Any((Drug drug2) => drug2.Id == batch.DrugId)).ToList();
			Batch fefo = source2.OrderBy((Batch batch) => batch.ExpiryDate ?? DateOnly.MaxValue).FirstOrDefault();
			Drug drug = ((fefo == null) ? drugForMedicine.FirstOrDefault() : drugForMedicine.First((Drug drug2) => drug2.Id == fefo.DrugId));
			decimal? item = ((fefo == null) ? medicine.ReferencePrice : (fefo.SalePrice ?? fefo.Mrp ?? drug?.SalePrice ?? drug?.Mrp ?? medicine.ReferencePrice));
			decimal? item2 = ((fefo != null && item.HasValue && item.GetValueOrDefault() > 0m && fefo.PurchasePrice > 0m) ? new decimal?(Math.Round((item.Value - fefo.PurchasePrice) / item.Value * 100m, 1, MidpointRounding.AwayFromZero)) : ((decimal?)null));
			return (Medicine: medicine, Drug: drug, Quantity: source2.Sum((Batch batch) => batch.Quantity), Expiry: fefo?.ExpiryDate, Price: item, Margin: item2);
		}).ToList();
		List<decimal> list = (from row in source
			where row.Price > (decimal?)0m
			select row.Price.Value).ToList();
		decimal cheapest = ((list.Count == 0) ? 0m : list.Min());
		decimal dearest = ((list.Count == 0) ? 0m : list.Max());
		List<decimal> list2 = (from row in source
			where row.Margin.HasValue
			select row.Margin.Value).ToList();
		decimal bestMargin = ((list2.Count == 0) ? 0m : list2.Max());
		decimal worstMargin = ((list2.Count == 0) ? 0m : list2.Min());
		int target = Math.Max(targetQuantity, 1);
		return (from item in source.Select(((CatalogMedicine Medicine, Drug Drug, decimal Quantity, DateOnly? Expiry, decimal? Price, decimal? Margin) row) =>
			{
				bool flag = row.Quantity >= (decimal)target;
				double num = ((row.Quantity <= 0m) ? 0.0 : (flag ? 50.0 : (25.0 * (double)(row.Quantity / (decimal)target))));
				double num2 = ((row.Price > (decimal?)0m) ? (30.0 * Scale(row.Price.Value, dearest, cheapest)) : 0.0);
				double num3 = (row.Margin.HasValue ? (20.0 * Scale(row.Margin.Value, worstMargin, bestMargin)) : 0.0);
				SubstituteMatchType substituteMatchType = ((!(row.Medicine.CompositionKey == key)) ? SubstituteMatchType.Compatible : SubstituteMatchType.Identical);
				return new MedicineSubstituteDto(row.Medicine.Id, row.Drug?.Id, row.Medicine.Name, string.Join(", ", new string[2]
				{
					row.Medicine.ShortComposition1,
					row.Medicine.ShortComposition2
				}.Where((string value) => !string.IsNullOrWhiteSpace(value))), row.Medicine.CompositionKey, row.Medicine.Manufacturer, row.Medicine.PackSizeLabel, habits.Contains(row.Medicine.Name.ToLowerInvariant()), substituteMatchType, row.Quantity, flag, row.Expiry, row.Price, row.Margin, Math.Round(num + num2 + num3, 2), Explain(row.Quantity, flag, row.Price, cheapest, row.Margin, bestMargin, substituteMatchType));
			})
			orderby item.Score descending, item.MatchType, item.CustomerPrice ?? decimal.MaxValue
			select item).ThenBy((MedicineSubstituteDto item) => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public Task<PrescriptionParseResultDto> ParsePrescriptionImageAsync(byte[] imageBytes)
	{
		ArgumentNullException.ThrowIfNull(imageBytes, "imageBytes");
		string message = (options.AllowExternalCalls ? "No AI provider is configured, so the prescription image was not read. Enter the items manually." : "Offline mode: reading prescription images is disabled. Enter the items manually.");
		return Task.FromResult(new PrescriptionParseResultDto
		{
			Source = "None",
			Message = message
		});
	}

	public async Task<InventoryForecastReportDto> GenerateStockAdvisoryAsync()
	{
		DateTime now = DateTime.UtcNow;
		DateTime since = now.AddDays(-30.0);
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		DateOnly nearExpiry = today.AddDays(60);
		Dictionary<Guid, decimal> sold = (from item in (await (from item in context.SaleItems.AsNoTracking()
				join sale in context.Sales.AsNoTracking() on item.SaleId equals sale.Id
				where sale.SaleAtUtc >= since
				select new { item.DrugId, item.Quantity }).ToListAsync()).Concat(await (from item in context.WholesaleInvoiceItems.AsNoTracking()
				join invoice in context.WholesaleInvoices.AsNoTracking() on item.WholesaleInvoiceId equals invoice.Id
				where invoice.Status == "Posted" && invoice.InvoiceAtUtc >= since
				select new
				{
					DrugId = item.DrugId,
					Quantity = item.Quantity + item.FreeQuantity
				}).ToListAsync())
			group item by item.DrugId).ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
		List<Drug> drugs = await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive
			select drug).ToListAsync();
		List<Batch> source = await (from batch in context.Batches.AsNoTracking()
			where batch.Quantity > 0m && (batch.ExpiryDate == null || batch.ExpiryDate >= today)
			select batch).ToListAsync();
		Dictionary<Guid, decimal> dictionary = (from batch in source
			group batch by batch.DrugId).ToDictionary((IGrouping<Guid, Batch> group) => group.Key, (IGrouping<Guid, Batch> group) => group.Sum((Batch batch) => batch.Quantity));
		Dictionary<Guid, decimal> dictionary2 = (from batch in source
			where batch.ExpiryDate.HasValue && batch.ExpiryDate <= nearExpiry
			group batch by batch.DrugId).ToDictionary((IGrouping<Guid, Batch> group) => group.Key, (IGrouping<Guid, Batch> group) => group.Sum((Batch batch) => batch.Quantity));
		List<StockAdvisoryItemDto> list = new List<StockAdvisoryItemDto>();
		foreach (Drug item in drugs)
		{
			decimal valueOrDefault = dictionary.GetValueOrDefault(item.Id);
			decimal num = Math.Round(sold.GetValueOrDefault(item.Id) / 30m, 2, MidpointRounding.AwayFromZero);
			decimal valueOrDefault2 = dictionary2.GetValueOrDefault(item.Id);
			if (!(num == 0m) || !(valueOrDefault2 == 0m) || (item.ReorderLevel > 0m && valueOrDefault < item.ReorderLevel))
			{
				decimal? num2 = ((num > 0m) ? new decimal?(Math.Round(valueOrDefault / num, 1, MidpointRounding.AwayFromZero)) : ((decimal?)null));
				StockAdvisoryStatus status = ((valueOrDefault <= 0m) ? StockAdvisoryStatus.StockOut : ((num2 < (decimal?)7m || (item.ReorderLevel > 0m && valueOrDefault < item.ReorderLevel)) ? StockAdvisoryStatus.Low : StockAdvisoryStatus.Healthy));
				decimal num3 = Math.Max(Math.Ceiling(Math.Max(num * 30m, item.ReorderLevel) - valueOrDefault), 0m);
				list.Add(new StockAdvisoryItemDto(item.Id, item.Name, valueOrDefault, num, num2, num3, valueOrDefault2, status, Advice(status, num3, num2, valueOrDefault2)));
			}
		}
		return new InventoryForecastReportDto(now, "Local heuristic (average daily sales)", 30, 30, (from item in list
			orderby item.Status descending, item.DaysOfCover ?? decimal.MaxValue
			select item).ThenBy((StockAdvisoryItemDto item) => item.Name, StringComparer.OrdinalIgnoreCase).ToList());
	}

	private static HashSet<string> SaltNames(string compositionKey)
	{
		return (from part in compositionKey.Split('|', StringSplitOptions.RemoveEmptyEntries)
			select string.Join(' ', from word in part.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				where !word.Any(char.IsDigit) && !Units.Contains(word)
				select word) into name
			where name.Length > 0
			select name).ToHashSet(StringComparer.Ordinal);
	}

	private static double Scale(decimal value, decimal worst, decimal best)
	{
		if (!(best == worst))
		{
			return (double)((value - worst) / (best - worst));
		}
		return 1.0;
	}

	private static string Explain(decimal quantity, bool covers, decimal? price, decimal cheapest, decimal? margin, decimal bestMargin, SubstituteMatchType match)
	{
		List<string> list = new List<string> { (match == SubstituteMatchType.Identical) ? "same salts and strength" : "same salts, different strength - confirm dose" };
		List<string> list2 = list;
		string item;
		if (quantity <= 0m)
		{
			item = "not in stock";
		}
		else
		{
			item = (covers ? "in stock (FEFO)" : $"only {quantity:0.##} in stock");
		}
		list2.Add(item);
		if (price > (decimal?)0m)
		{
			decimal? num = price;
			decimal num2 = cheapest;
			if ((num.GetValueOrDefault() == num2) & num.HasValue)
			{
				list.Add("lowest price");
			}
		}
		if (margin.HasValue)
		{
			decimal? num = margin;
			decimal num2 = bestMargin;
			if ((num.GetValueOrDefault() == num2) & num.HasValue)
			{
				list.Add("best margin");
			}
		}
		return string.Join("; ", list);
	}

	private static string Advice(StockAdvisoryStatus status, decimal reorder, decimal? cover, decimal nearExpiry)
	{
		List<string> list = new List<string>();
		switch (status)
		{
		case StockAdvisoryStatus.StockOut:
			list.Add($"Out of stock; reorder about {reorder:0.##}");
			break;
		case StockAdvisoryStatus.Low:
			list.Add($"Low ({cover?.ToString("0.#") ?? "-"} days of cover); reorder about {reorder:0.##}");
			break;
		default:
			list.Add("Stock is sufficient");
			break;
		}
		if (nearExpiry > 0m)
		{
			list.Add($"{nearExpiry:0.##} expiring within {60} days - sell first or return");
		}
		return string.Join(". ", list);
	}
}
