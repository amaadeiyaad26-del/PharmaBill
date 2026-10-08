using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class BranchService(IUnitOfWork unitOfWork, BranchSettingsStore settingsStore, DatabaseStorageOptions storage)
{
	public const string DefaultBranchCode = "BR01-MAIN";

	public const string SnapshotFileName = "snapshot.db.enc";

	public const string StockIndexFileName = "stock-index.json";

	public const string SalesSummaryFileName = "sales-summary.json";

	public BranchSettings CurrentSettings => settingsStore.Load();

	public Guid? CurrentBranchId => settingsStore.Load().CurrentBranchId;

	public bool IsHeadOffice => settingsStore.Load().IsHeadOffice;

	public string BranchCacheDirectory => Path.Combine(storage.RootDirectory, "branch-cache");

	public async Task<Branch> EnsureCurrentBranchAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		BranchSettings settings = settingsStore.Load();
		PharmaBillDbContext context = unitOfWork.Context;
		Branch branch = null;
		if (settings.CurrentBranchId.HasValue)
		{
			branch = await context.Branches.SingleOrDefaultAsync((Branch item) => item.Id == settings.CurrentBranchId.Value, cancellationToken);
		}
		if (branch == null)
		{
			branch = await (from item in context.Branches
				orderby item.IsHeadOffice descending, item.Code
				select item).FirstOrDefaultAsync((Branch item) => item.IsActive, cancellationToken);
		}
		if (branch == null)
		{
			PharmacyProfile pharmacyProfile = await context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
			string text = (string.IsNullOrWhiteSpace(settings.CurrentBranchCode) ? "BR01-MAIN" : settings.CurrentBranchCode.Trim().ToUpperInvariant());
			branch = new Branch
			{
				Code = text,
				BranchName = ((!string.IsNullOrWhiteSpace(settings.CurrentBranchName)) ? settings.CurrentBranchName.Trim() : (pharmacyProfile?.Name ?? "Main branch")),
				Address = pharmacyProfile?.Address,
				ContactPhone = pharmacyProfile?.Phone,
				Gstin = pharmacyProfile?.Gstin,
				IsHeadOffice = settings.IsHeadOffice,
				InvoicePrefix = BuildInvoicePrefix(text),
				IsActive = true
			};
			context.Branches.Add(branch);
			await unitOfWork.SaveChangesAsync(cancellationToken);
		}
		settingsStore.Save(new BranchSettings
		{
			CurrentBranchId = branch.Id,
			CurrentBranchCode = branch.Code,
			CurrentBranchName = branch.BranchName,
			IsHeadOffice = branch.IsHeadOffice
		});
		return branch;
	}

	public async Task<IReadOnlyList<BranchOption>> ListOptionsAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		Guid? currentId = CurrentBranchId;
		return (await (from item in unitOfWork.Context.Branches.AsNoTracking()
			where item.IsActive
			orderby item.Code
			select item).ToListAsync(cancellationToken)).Select((Branch item) => new BranchOption(item.Id, item.Code, item.BranchName, item.IsHeadOffice, currentId.HasValue && item.Id == currentId.Value)).ToArray();
	}

	public async Task<string> ResolveInvoiceSeriesPrefixAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		Branch branch = await EnsureCurrentBranchAsync(cancellationToken);
		if (!string.IsNullOrWhiteSpace(branch.InvoicePrefix))
		{
			return branch.InvoicePrefix.Trim();
		}
		PharmacyProfile pharmacyProfile = await unitOfWork.Context.PharmacyProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
		if (!string.IsNullOrWhiteSpace(pharmacyProfile?.InvoicePrefix))
		{
			return pharmacyProfile.InvoicePrefix.Trim();
		}
		return BuildInvoicePrefix(branch.Code);
	}

	/// <summary>
	/// B2B wholesale tax-invoice series (e.g. WS/2026-27/000001). Kept separate from retail cash-memo numbering.
	/// </summary>
	public Task<string> ResolveWholesaleInvoiceSeriesPrefixAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = cancellationToken;
		return Task.FromResult(WholesaleInvoicePrefix);
	}

	public const string WholesaleInvoicePrefix = "WS";

	public static string BuildInvoicePrefix(string branchCode)
	{
		string text = (string.IsNullOrWhiteSpace(branchCode) ? "BR01-MAIN" : branchCode.Trim().ToUpperInvariant());
		return (text.Contains('-', StringComparison.Ordinal) ? text.Split('-', 2)[0] : text) + "-INV";
	}

	public async Task UpdateCurrentBranchAsync(string code, string name, string? address, string? phone, string? gstin, string? drugLicenseNo, bool isHeadOffice, CancellationToken cancellationToken = default(CancellationToken))
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(code, "code");
		ArgumentException.ThrowIfNullOrWhiteSpace(name, "name");
		Branch branch = await EnsureCurrentBranchAsync(cancellationToken);
		branch.Code = code.Trim().ToUpperInvariant();
		branch.BranchName = name.Trim();
		branch.Address = NullIfWhiteSpace(address);
		branch.ContactPhone = NullIfWhiteSpace(phone);
		branch.Gstin = NullIfWhiteSpace(gstin);
		branch.DrugLicenseNo = NullIfWhiteSpace(drugLicenseNo);
		branch.IsHeadOffice = isHeadOffice;
		branch.InvoicePrefix = BuildInvoicePrefix(branch.Code);
		await unitOfWork.SaveChangesAsync(cancellationToken);
		settingsStore.Save(new BranchSettings
		{
			CurrentBranchId = branch.Id,
			CurrentBranchCode = branch.Code,
			CurrentBranchName = branch.BranchName,
			IsHeadOffice = branch.IsHeadOffice
		});
	}

	public async Task<BranchStockIndexDocument> BuildLocalStockIndexAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		Branch branch = await EnsureCurrentBranchAsync(cancellationToken);
		PharmaBillDbContext context = unitOfWork.Context;
		var quantities = await (from item in context.StockMovements.AsNoTracking()
			group item by item.DrugId into @group
			select new
			{
				DrugId = @group.Key,
				Quantity = @group.Sum((StockMovement item) => item.QuantityChange)
			} into item
			where item.Quantity > 0m
			select item).ToListAsync(cancellationToken);
		Guid[] drugIds = quantities.Select(item => item.DrugId).ToArray();
		Dictionary<Guid, Drug> drugs = await (from item in context.Drugs.AsNoTracking()
			where drugIds.Contains(item.Id)
			select item).ToDictionaryAsync((Drug item) => item.Id, cancellationToken);
		Guid[] catalogIds = (from item in drugs.Values
			where item.CatalogMedicineId.HasValue
			select item.CatalogMedicineId.Value).Distinct().ToArray();
		Dictionary<Guid, CatalogMedicine> catalogs = await (from item in context.CatalogMedicines.AsNoTracking()
			where catalogIds.Contains(item.Id)
			select item).ToDictionaryAsync((CatalogMedicine item) => item.Id, cancellationToken);
		return new BranchStockIndexDocument
		{
			BranchId = branch.Id,
			BranchCode = branch.Code,
			BranchName = branch.BranchName,
			GeneratedAtUtc = DateTime.UtcNow,
			Items = (from item in quantities.Select(item =>
				{
					drugs.TryGetValue(item.DrugId, out var value);
					string compositionKey = null;
					Guid? guid = value?.CatalogMedicineId;
					if (guid.HasValue)
					{
						Guid valueOrDefault = guid.GetValueOrDefault();
						if (catalogs.TryGetValue(valueOrDefault, out var value2))
						{
							compositionKey = value2.CompositionKey;
						}
					}
					return new BranchStockIndexItem
					{
						DrugId = item.DrugId,
						MedicineName = (value?.Name ?? item.DrugId.ToString("N")),
						CompositionKey = compositionKey,
						Quantity = item.Quantity
					};
				})
				orderby item.MedicineName
				select item).ToList()
		};
	}

	public async Task<BranchSalesSummaryDocument> BuildLocalSalesSummaryAsync(DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		Branch branch = await EnsureCurrentBranchAsync(cancellationToken);
		DateOnly end = to ?? DateOnly.FromDateTime(DateTime.Today);
		DateOnly start = from ?? end.AddMonths(-1);
		DateTime startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		DateTime endUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
		List<decimal> list = await (from sale in unitOfWork.Context.Sales.AsNoTracking()
			where !sale.IsDeleted && sale.SaleAtUtc >= startUtc && sale.SaleAtUtc < endUtc && (sale.BranchId == null || sale.BranchId == branch.Id)
			select sale.TotalAmount).ToListAsync(cancellationToken);
		return new BranchSalesSummaryDocument
		{
			BranchId = branch.Id,
			BranchCode = branch.Code,
			BranchName = branch.BranchName,
			GeneratedAtUtc = DateTime.UtcNow,
			FromDate = start,
			ToDate = end,
			InvoiceCount = list.Count,
			SalesTotal = list.Sum()
		};
	}

	public void SavePeerStockIndex(BranchStockIndexDocument document)
	{
		ArgumentNullException.ThrowIfNull(document, "document");
		Directory.CreateDirectory(BranchCacheDirectory);
		File.WriteAllText(Path.Combine(BranchCacheDirectory, Sanitize(document.BranchCode) + "-stock-index.json"), JsonSerializer.Serialize(document, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	public void SavePeerSalesSummary(BranchSalesSummaryDocument document)
	{
		ArgumentNullException.ThrowIfNull(document, "document");
		Directory.CreateDirectory(BranchCacheDirectory);
		File.WriteAllText(Path.Combine(BranchCacheDirectory, Sanitize(document.BranchCode) + "-sales-summary.json"), JsonSerializer.Serialize(document, new JsonSerializerOptions
		{
			WriteIndented = true
		}));
	}

	public IReadOnlyList<BranchStockIndexDocument> LoadPeerStockIndexes()
	{
		if (!Directory.Exists(BranchCacheDirectory))
		{
			return Array.Empty<BranchStockIndexDocument>();
		}
		string currentBranchCode = settingsStore.Load().CurrentBranchCode;
		List<BranchStockIndexDocument> list = new List<BranchStockIndexDocument>();
		foreach (string item in Directory.EnumerateFiles(BranchCacheDirectory, "*-stock-index.json"))
		{
			try
			{
				BranchStockIndexDocument branchStockIndexDocument = JsonSerializer.Deserialize<BranchStockIndexDocument>(File.ReadAllText(item));
				if (branchStockIndexDocument != null && !string.Equals(branchStockIndexDocument.BranchCode, currentBranchCode, StringComparison.OrdinalIgnoreCase))
				{
					list.Add(branchStockIndexDocument);
				}
			}
			catch
			{
			}
		}
		return list;
	}

	public IReadOnlyList<BranchSalesSummaryDocument> LoadPeerSalesSummaries()
	{
		if (!Directory.Exists(BranchCacheDirectory))
		{
			return Array.Empty<BranchSalesSummaryDocument>();
		}
		string currentBranchCode = settingsStore.Load().CurrentBranchCode;
		List<BranchSalesSummaryDocument> list = new List<BranchSalesSummaryDocument>();
		foreach (string item in Directory.EnumerateFiles(BranchCacheDirectory, "*-sales-summary.json"))
		{
			try
			{
				BranchSalesSummaryDocument branchSalesSummaryDocument = JsonSerializer.Deserialize<BranchSalesSummaryDocument>(File.ReadAllText(item));
				if (branchSalesSummaryDocument != null && !string.Equals(branchSalesSummaryDocument.BranchCode, currentBranchCode, StringComparison.OrdinalIgnoreCase))
				{
					list.Add(branchSalesSummaryDocument);
				}
			}
			catch
			{
			}
		}
		return list;
	}

	public IReadOnlyList<PeerBranchStockHint> FindPeerAvailability(string? medicineName, string? compositionKey, Guid? drugId = null)
	{
		List<PeerBranchStockHint> list = new List<PeerBranchStockHint>();
		foreach (BranchStockIndexDocument item in LoadPeerStockIndexes())
		{
			foreach (BranchStockIndexItem item2 in item.Items.Where((BranchStockIndexItem entry) => entry.Quantity > 0m))
			{
				bool num = drugId.HasValue && item2.DrugId == drugId.Value;
				bool flag = !string.IsNullOrWhiteSpace(compositionKey) && !string.IsNullOrWhiteSpace(item2.CompositionKey) && string.Equals(item2.CompositionKey, compositionKey, StringComparison.OrdinalIgnoreCase);
				bool flag2 = !string.IsNullOrWhiteSpace(medicineName) && item2.MedicineName.Contains(medicineName.Trim(), StringComparison.OrdinalIgnoreCase);
				if (num || flag || flag2)
				{
					list.Add(new PeerBranchStockHint(item.BranchCode, item.BranchName, item2.MedicineName, item2.CompositionKey, item2.Quantity));
				}
			}
		}
		return list.OrderByDescending((PeerBranchStockHint item) => item.Quantity).Take(5).ToArray();
	}

	public static string FormatPeerAvailabilityHint(IReadOnlyList<PeerBranchStockHint> hints)
	{
		if (hints.Count == 0)
		{
			return string.Empty;
		}
		return string.Join(" · ", hints.Select((PeerBranchStockHint hint) => $"{hint.BranchName} has {hint.Quantity:0.##} in stock"));
	}

	public static string CloudBranchFolderRelativePath(string branchCode)
	{
		return Path.Combine("Branches", Sanitize(branchCode)).Replace('\\', '/');
	}

	private static string Sanitize(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		return new string((from ch in value.Trim()
			select (!invalid.Contains(ch)) ? ch : '_').ToArray());
	}

	private static string? NullIfWhiteSpace(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return null;
	}
}
