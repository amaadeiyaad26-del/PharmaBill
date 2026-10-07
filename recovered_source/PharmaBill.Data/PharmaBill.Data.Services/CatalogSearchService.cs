using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Catalog;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class CatalogSearchService(PharmaBillDbContext context, BranchService branchService)
{
	public const int MaxResultsPerGroup = 25;

	private const int MaxSubstituteCandidates = 400;

	public async Task<MedicineSearchResults> SearchAsync(string query, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(query, "query");
		if (query.Trim().Length < 2)
		{
			return new MedicineSearchResults(Array.Empty<MedicineSearchResult>(), Array.Empty<MedicineSearchResult>());
		}
		string text = BuildFtsQuery(query);
		if (text.Length == 0)
		{
			return new MedicineSearchResults(Array.Empty<MedicineSearchResult>(), Array.Empty<MedicineSearchResult>());
		}
		List<Guid> matchingIds = await FindCatalogIdsAsync(text, cancellationToken);
		if (matchingIds.Count == 0)
		{
			return new MedicineSearchResults(Array.Empty<MedicineSearchResult>(), Array.Empty<MedicineSearchResult>());
		}
		List<CatalogMedicine> medicines = await (from medicine in context.CatalogMedicines.AsNoTracking()
			where matchingIds.Contains(medicine.Id) && !medicine.IsDiscontinued
			select medicine).ToListAsync(cancellationToken);
		string[] localNames = medicines.Select((CatalogMedicine medicine) => medicine.Name.ToLower()).Distinct().ToArray();
		HashSet<string> habits = await (from info in context.CatalogInfos.AsNoTracking()
			where info.IsHabitForming && localNames.Contains(info.NameKey)
			select info.NameKey).ToHashSetAsync(cancellationToken);
		var drugLinks = await (from drug in context.Drugs.AsNoTracking()
			where drug.CatalogMedicineId.HasValue && matchingIds.Contains(drug.CatalogMedicineId.Value) && drug.IsActive
			select new { drug.Id, drug.CatalogMedicineId, drug.Name }).ToListAsync(cancellationToken);
		Dictionary<Guid, decimal> stockByDrug = await GetLiveStockByDrugAsync(drugLinks.Select(link => link.Id).ToArray(), cancellationToken);
		Dictionary<Guid, decimal> stockByCatalogId = (from link in drugLinks
			where stockByDrug.GetValueOrDefault(link.Id) > 0m
			group link by link.CatalogMedicineId.Value).ToDictionary(group => group.Key, group => group.Sum(link => stockByDrug.GetValueOrDefault(link.Id)));
		Dictionary<Guid, (Guid? DrugId, string? Rack, decimal? Mrp, DateOnly? NearestExpiry)> shelfByCatalog = await GetShelfAndMrpByCatalogAsync(drugLinks.Select(link => (Id: link.Id, Value: link.CatalogMedicineId.Value)).ToArray(), stockByDrug, cancellationToken);
		HashSet<Guid> linkedCatalogIds = (from link in drugLinks
			where stockByDrug.GetValueOrDefault(link.Id) > 0m
			select link.CatalogMedicineId.Value).ToHashSet();
		MedicineSearchResult[] source = medicines.OrderBy((CatalogMedicine medicine) => matchingIds.IndexOf(medicine.Id)).Select((CatalogMedicine medicine) =>
		{
			shelfByCatalog.TryGetValue(medicine.Id, out (Guid?, string, decimal?, DateOnly?) value);
			decimal valueOrDefault = stockByCatalogId.GetValueOrDefault(medicine.Id);
			string otherBranchAvailability = ((valueOrDefault <= 0m) ? BranchService.FormatPeerAvailabilityHint(branchService.FindPeerAvailability(medicine.Name, medicine.CompositionKey, value.Item1)) : null);
			return ToResult(medicine, habits.Contains(medicine.Name.ToLowerInvariant()), valueOrDefault, value.Item4, value.Item2, value.Item3, value.Item1, otherBranchAvailability);
		}).ToArray();
		return new MedicineSearchResults(source.Where((MedicineSearchResult result) => result.IsInStock).Take(25).ToArray(), source.Where((MedicineSearchResult result) => !linkedCatalogIds.Contains(result.CatalogMedicineId)).Take(25).ToArray());
	}

	public async Task<List<SubstituteStockResult>> FindSubstitutesAsync(string saltComposition, Guid? excludeCatalogMedicineId = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentNullException.ThrowIfNull(saltComposition, "saltComposition");
		string text = CompositionKeyBuilder.Build(saltComposition.Split(new char[4] { '+', '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		if (text.Length == 0)
		{
			text = CompositionKeyBuilder.Build(saltComposition);
		}
		if (text.Length == 0)
		{
			return new List<SubstituteStockResult>();
		}
		HashSet<string> requestedSalts = SaltNames(text);
		if (requestedSalts.Count == 0)
		{
			return new List<SubstituteStockResult>();
		}
		string prefilter = requestedSalts.OrderByDescending((string item) => item.Length).First();
		List<CatalogMedicine> candidates = (await (from medicine in context.CatalogMedicines.AsNoTracking()
			where !medicine.IsDiscontinued && medicine.CompositionKey.Length > 0 && medicine.CompositionKey.Contains(prefilter) && (!((Guid?)excludeCatalogMedicineId).HasValue || medicine.Id != ((Guid?)excludeCatalogMedicineId).Value)
			select medicine).Take(400).ToListAsync(cancellationToken)).Where((CatalogMedicine medicine) => SaltNames(medicine.CompositionKey).SetEquals(requestedSalts)).ToList();
		if (candidates.Count == 0)
		{
			return new List<SubstituteStockResult>();
		}
		Guid[] ids = candidates.Select((CatalogMedicine item) => item.Id).ToArray();
		var localLinks = await (from drug in context.Drugs.AsNoTracking()
			where drug.IsActive && drug.CatalogMedicineId.HasValue && ids.Contains(drug.CatalogMedicineId.Value)
			select new
			{
				Id = drug.Id,
				CatalogId = drug.CatalogMedicineId.Value,
				Mrp = drug.Mrp,
				SalePrice = drug.SalePrice
			}).ToListAsync(cancellationToken);
		Dictionary<Guid, decimal> stockByDrug = await GetLiveStockByDrugAsync(localLinks.Select(link => link.Id).ToArray(), cancellationToken);
		Dictionary<Guid, (Guid? DrugId, string? Rack, decimal? Mrp, DateOnly? NearestExpiry)> shelfByCatalog = await GetShelfAndMrpByCatalogAsync(localLinks.Select(link => (Id: link.Id, CatalogId: link.CatalogId)).ToArray(), stockByDrug, cancellationToken);
		Dictionary<Guid, decimal> stockByCatalog = (from link in localLinks
			group link by link.CatalogId).ToDictionary(group => group.Key, group => group.Sum(link => stockByDrug.GetValueOrDefault(link.Id)));
		string[] names = candidates.Select((CatalogMedicine item) => item.Name.ToLowerInvariant()).Distinct().ToArray();
		HashSet<string> habits = await (from info in context.CatalogInfos.AsNoTracking()
			where info.IsHabitForming && names.Contains(info.NameKey)
			select info.NameKey).ToHashSetAsync(cancellationToken);
		return (from item in candidates.Select((CatalogMedicine item) =>
			{
				shelfByCatalog.TryGetValue(item.Id, out (Guid?, string, decimal?, DateOnly?) value);
				decimal valueOrDefault = stockByCatalog.GetValueOrDefault(item.Id);
				decimal? mrp = value.Item3 ?? item.ReferencePrice;
				return new SubstituteStockResult(item.Id, value.Item1, item.Name, JoinComposition(item.ShortComposition1, item.ShortComposition2), item.CompositionKey, valueOrDefault, mrp, value.Item2, item.PackSizeLabel, item.Manufacturer, habits.Contains(item.Name.ToLowerInvariant()));
			})
			orderby item.CurrentStock > 0m descending
			select item).ThenBy((SubstituteStockResult item) => item.BrandName, StringComparer.OrdinalIgnoreCase).Take(25).ToList();
	}

	public async Task<IReadOnlyList<MedicineSearchResult>> FindSubstitutesByExactKeyAsync(string compositionKey, Guid excludeCatalogMedicineId, CancellationToken cancellationToken = default(CancellationToken))
	{
		return (await FindSubstitutesAsync(compositionKey, excludeCatalogMedicineId, cancellationToken)).Select(ToMedicineSearchResult).ToArray();
	}

	public static MedicineSearchResult ToMedicineSearchResult(SubstituteStockResult item)
	{
		return new MedicineSearchResult(item.CatalogMedicineId, item.BrandName, item.SaltStrength, item.CompositionKey, item.Manufacturer, item.PackSize, item.Mrp, item.IsHabitForming, item.CurrentStock, item.IsInStock, null, item.Rack, item.Mrp, item.DrugId);
	}

	private async Task<Dictionary<Guid, decimal>> GetLiveStockByDrugAsync(Guid[] drugIds, CancellationToken cancellationToken)
	{
		if (drugIds.Length == 0)
		{
			return new Dictionary<Guid, decimal>();
		}
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		Guid[] batches = await (from batch in context.Batches.AsNoTracking()
			where drugIds.Contains(batch.DrugId) && (batch.ExpiryDate == null || batch.ExpiryDate >= today)
			select batch.Id).ToArrayAsync(cancellationToken);
		if (batches.Length == 0)
		{
			return new Dictionary<Guid, decimal>();
		}
		Dictionary<Guid, decimal> movementTotals = await (from movement in context.StockMovements.AsNoTracking()
			where batches.Contains(movement.BatchId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement item) => item.QuantityChange)
			}).ToDictionaryAsync(item => item.BatchId, item => item.Quantity, cancellationToken);
		return (from batch in await (from batch in context.Batches.AsNoTracking()
				where batches.Contains(batch.Id)
				select new { batch.Id, batch.DrugId }).ToListAsync(cancellationToken)
			group batch by batch.DrugId).ToDictionary(group => group.Key, group => group.Sum(batch => Math.Max(0m, movementTotals.GetValueOrDefault(batch.Id))));
	}

	private async Task<Dictionary<Guid, (Guid? DrugId, string? Rack, decimal? Mrp, DateOnly? NearestExpiry)>> GetShelfAndMrpByCatalogAsync((Guid DrugId, Guid CatalogId)[] links, IReadOnlyDictionary<Guid, decimal> stockByDrug, CancellationToken cancellationToken)
	{
		Dictionary<Guid, (Guid? DrugId, string? Rack, decimal? Mrp, DateOnly? NearestExpiry)> result = new Dictionary<Guid, (Guid?, string, decimal?, DateOnly?)>();
		if (links.Length == 0)
		{
			return result;
		}
		Guid[] drugIds = links.Select(((Guid DrugId, Guid CatalogId) link) => link.DrugId).Distinct().ToArray();
		DateOnly today = DateOnly.FromDateTime(DateTime.Today);
		List<Batch> batches = await (from batch2 in context.Batches.AsNoTracking()
			where drugIds.Contains(batch2.DrugId) && (batch2.ExpiryDate == null || batch2.ExpiryDate >= today)
			select batch2).ToListAsync(cancellationToken);
		Guid[] batchIds = batches.Select((Batch batch2) => batch2.Id).ToArray();
		Dictionary<Guid, decimal> dictionary = ((batchIds.Length != 0) ? (await (from movement in context.StockMovements.AsNoTracking()
			where batchIds.Contains(movement.BatchId)
			group movement by movement.BatchId into @group
			select new
			{
				BatchId = @group.Key,
				Quantity = @group.Sum((StockMovement stockMovement) => stockMovement.QuantityChange)
			}).ToDictionaryAsync(anon => anon.BatchId, anon => anon.Quantity, cancellationToken)) : new Dictionary<Guid, decimal>());
		Dictionary<Guid, decimal> quantities = dictionary;
		foreach (IGrouping<Guid, (Guid, Guid)> catalogGroup in from link in links
			group link by link.CatalogId)
		{
			Batch batch = (from batch2 in batches
				where catalogGroup.Any(((Guid DrugId, Guid CatalogId) link) => link.DrugId == batch2.DrugId) && quantities.GetValueOrDefault(batch2.Id) > 0m
				orderby batch2.ExpiryDate ?? DateOnly.MaxValue
				select batch2).ThenBy((Batch batch2) => batch2.BatchNo, StringComparer.OrdinalIgnoreCase).ToArray().FirstOrDefault();
			if (batch == null)
			{
				Guid? item = ((IEnumerable<(Guid, Guid)>)catalogGroup).Select((Func<(Guid, Guid), Guid?>)(((Guid DrugId, Guid CatalogId) link) => link.DrugId)).FirstOrDefault();
				result[catalogGroup.Key] = (item, null, null, null);
			}
			else
			{
				result[catalogGroup.Key] = (batch.DrugId, string.IsNullOrWhiteSpace(batch.Rack) ? null : batch.Rack.Trim(), batch.Mrp ?? batch.SalePrice, batch.ExpiryDate);
			}
		}
		return result;
	}

	private static HashSet<string> SaltNames(string compositionKey)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		string[] array = compositionKey.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text in array)
		{
			string[] array2 = (from token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				where !token.Any(char.IsDigit) && !token.Equals("mg", StringComparison.OrdinalIgnoreCase) && !token.Equals("mcg", StringComparison.OrdinalIgnoreCase) && !token.Equals("g", StringComparison.OrdinalIgnoreCase) && !token.Equals("ml", StringComparison.OrdinalIgnoreCase) && !token.Equals("iu", StringComparison.OrdinalIgnoreCase)
				select token).ToArray();
			if (array2.Length != 0)
			{
				hashSet.Add(string.Join(' ', array2));
			}
			else if (text.Length > 0)
			{
				hashSet.Add(text);
			}
		}
		return hashSet;
	}

	private async Task<List<Guid>> FindCatalogIdsAsync(string ftsQuery, CancellationToken cancellationToken)
	{
		DbConnection connection = context.Database.GetDbConnection();
		bool closeConnection = connection.State != ConnectionState.Open;
		if (closeConnection)
		{
			await connection.OpenAsync(cancellationToken);
		}
		List<Guid> result3;
		try
		{
			List<Guid> list2;
			await using (DbCommand command = connection.CreateCommand())
			{
				command.CommandText = "SELECT MedicineId\r\nFROM CatalogMedicineFts\r\nWHERE CatalogMedicineFts MATCH $query\r\nLIMIT 200";
				DbParameter dbParameter = command.CreateParameter();
				dbParameter.ParameterName = "$query";
				dbParameter.Value = ftsQuery;
				command.Parameters.Add(dbParameter);
				List<Guid> result = new List<Guid>(200);
				List<Guid> list;
				await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
				{
					while (await reader.ReadAsync(cancellationToken))
					{
						if (Guid.TryParseExact(reader.GetString(0), "N", out var result2))
						{
							result.Add(result2);
						}
					}
					list = result;
				}
				list2 = list;
			}
			result3 = list2;
		}
		finally
		{
			if (closeConnection)
			{
				await connection.CloseAsync();
			}
		}
		return result3;
	}

	private static string BuildFtsQuery(string query)
	{
		string[] value = (from token in (from token in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
				select new string(token.Where(char.IsLetterOrDigit).ToArray()) into token
				where token.Length > 0
				select token).Take(5)
			select "\"" + token.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"*").ToArray();
		return string.Join(" AND ", value);
	}

	private static MedicineSearchResult ToResult(CatalogMedicine medicine, bool habitForming, decimal stockQuantity, DateOnly? nearestExpiry = null, string? rack = null, decimal? mrp = null, Guid? drugId = null, string? otherBranchAvailability = null)
	{
		return new MedicineSearchResult(medicine.Id, medicine.Name, JoinComposition(medicine.ShortComposition1, medicine.ShortComposition2), medicine.CompositionKey, medicine.Manufacturer, medicine.PackSizeLabel, medicine.ReferencePrice, habitForming, stockQuantity, stockQuantity > 0m, nearestExpiry, rack, mrp ?? medicine.ReferencePrice, drugId, string.IsNullOrWhiteSpace(otherBranchAvailability) ? null : otherBranchAvailability);
	}

	private static string? JoinComposition(string? first, string? second)
	{
		string[] array = new string[2] { first, second }.Where((string value) => !string.IsNullOrWhiteSpace(value)).ToArray();
		if (array.Length != 0)
		{
			return string.Join(", ", array);
		}
		return null;
	}
}
