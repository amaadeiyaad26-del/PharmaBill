using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed class CatalogueService(IServiceScopeFactory scopeFactory)
{
	public async Task<CatalogueBarcodeMatch?> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(barcode))
		{
			return null;
		}
		string term = barcode.Trim();
		using IServiceScope scope = scopeFactory.CreateScope();
		PharmaBillDbContext context = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
		var anon = await (from item in context.Drugs.AsNoTracking()
			where item.IsActive && item.Barcode == term
			select new { item.Id, item.Name, item.BrandName, item.Strength, item.Barcode }).FirstOrDefaultAsync(cancellationToken);
		if (anon != null)
		{
			return new CatalogueBarcodeMatch(anon.Id, anon.Name, anon.BrandName, anon.Strength, anon.Barcode, null);
		}
		var anon2 = await (from b in context.Batches.AsNoTracking()
			join d in context.Drugs.AsNoTracking() on b.DrugId equals d.Id
			where b.BatchNo == term && d.IsActive
			select new { d.Id, d.Name, d.BrandName, d.Strength, d.Barcode, b.BatchNo }).FirstOrDefaultAsync(cancellationToken);
		return (anon2 == null) ? null : new CatalogueBarcodeMatch(anon2.Id, anon2.Name, anon2.BrandName, anon2.Strength, anon2.Barcode, anon2.BatchNo);
	}

	public async Task EnsureCatalogueSeededAsync(Action<CatalogueSeedProgress>? progress = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		string catalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_catalog.csv");
		string infoPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_info.csv");
		if (!File.Exists(catalogPath) && !File.Exists(infoPath))
		{
			progress?.Invoke(new CatalogueSeedProgress(100, 0L));
			return;
		}
		using (IServiceScope scope = scopeFactory.CreateScope())
		{
			PharmaBillDbContext requiredService = scope.ServiceProvider.GetRequiredService<PharmaBillDbContext>();
			CatalogImportService importer = scope.ServiceProvider.GetRequiredService<CatalogImportService>();
			List<CatalogImportState> source = await requiredService.CatalogImportStates.AsNoTracking().ToListAsync(cancellationToken);
			bool num = !File.Exists(catalogPath) || source.Any((CatalogImportState state) => state.ImportKey == "catalog" && state.Status.StartsWith("Completed", StringComparison.Ordinal));
			bool flag = !File.Exists(infoPath) || source.Any((CatalogImportState state) => state.ImportKey == "info" && state.Status.StartsWith("Completed", StringComparison.Ordinal));
			if (num & flag)
			{
				long processedItems = source.Sum((CatalogImportState state) => state.ImportedRows);
				progress?.Invoke(new CatalogueSeedProgress(100, processedItems));
				return;
			}
			importer.ProgressChanged += OnProgress;
			try
			{
				await importer.ImportAllAsync(File.Exists(catalogPath) ? catalogPath : string.Empty, File.Exists(infoPath) ? infoPath : string.Empty, cancellationToken);
				progress?.Invoke(new CatalogueSeedProgress(100, 253973L));
			}
			finally
			{
				importer.ProgressChanged -= OnProgress;
			}
		}
		void OnProgress(object? _, CatalogImportProgress update)
		{
			progress?.Invoke(new CatalogueSeedProgress(update.Percentage, update.ProcessedRows));
		}
	}
}
