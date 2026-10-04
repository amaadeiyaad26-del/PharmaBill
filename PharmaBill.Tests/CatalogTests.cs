using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Catalog;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

public sealed class CatalogTests
{
    [Fact]
    public void CompositionKeyBuilder_NormalizesAndSortsSaltsAndStrengths()
    {
        var first = CompositionKeyBuilder.Build(" Clavulanic Acid (125mg)", "Amoxycillin (500mg)");
        var reordered = CompositionKeyBuilder.Build("AMOXYCILLIN (500mg)", "clavulanic acid (125mg)");

        Assert.Equal("amoxycillin 500mg|clavulanic acid 125mg", first);
        Assert.Equal(first, reordered);
        Assert.Equal(
            "amoxycillin 500mg",
            CompositionKeyBuilder.Build("Amoxycillin (500mg)", "NA", string.Empty));
    }

    [Fact]
    public async Task Importer_HandlesQuotedCommasEmptyCompositionAndCrLf_ThenFtsSearches()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.CatalogTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var catalogPath = Path.Combine(directory, "medicine_catalog.csv");
            var infoPath = Path.Combine(directory, "medicine_info.csv");
            await File.WriteAllTextAsync(
                catalogPath,
                "id,name,price(\u20B9),Is_discontinued,manufacturer_name,type,pack_size_label,short_composition1,short_composition2\r\n" +
                "1,\"Test, Brand 500\",12.50,FALSE,\"Maker, India\",allopathy,strip of 10,\"Drug A (500mg)\",\r\n" +
                "2,Test Syrup,NA,FALSE,Maker Two,allopathy,100 ml,Drug B (5mg),NA\r\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await File.WriteAllTextAsync(
                infoPath,
                "name_key,habit_forming,therapeutic_class,chemical_class,action_class,use\r\n" +
                "\"test, brand 500\",Y,CLASS,NA,ACTION,\"Use, with care\"\r\n" +
                "test syrup,N,CLASS,NA,NA,Use\r\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var importer = new CatalogImportService(database.Context);
            await importer.ImportAllAsync(catalogPath, infoPath);

            var medicine = await database.Context.CatalogMedicines
                .SingleAsync(item => item.SourceId == "1");
            Assert.Equal("Test, Brand 500", medicine.Name);
            Assert.Equal("Maker, India", medicine.Manufacturer);
            Assert.Null(medicine.ShortComposition2);
            Assert.Equal(12.50m, medicine.ReferencePrice);
            Assert.Equal("drug a 500mg", medicine.CompositionKey);
            Assert.Null((await database.Context.CatalogMedicines.SingleAsync(item => item.SourceId == "2")).ReferencePrice);

            var stopwatch = Stopwatch.StartNew();
            var results = await new CatalogSearchService(database.Context).SearchAsync("Brand");
            stopwatch.Stop();
            Assert.Single(results.FromCatalog);
            Assert.Equal("Test, Brand 500", results.FromCatalog[0].Name);
            Assert.True(results.FromCatalog[0].IsHabitForming);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(200),
                $"Prefix FTS search took {stopwatch.Elapsed.TotalMilliseconds:N0} ms.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Importer_ReimportIsSafeAndUsesCommittedCheckpointsForResume()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.CatalogResume.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var catalogPath = Path.Combine(directory, "medicine_catalog.csv");
            var csv = new StringBuilder(
                "id,name,price(\u20B9),Is_discontinued,manufacturer_name,type,pack_size_label,short_composition1,short_composition2\r\n");
            for (var index = 1; index <= 1001; index++)
            {
                csv.Append(index).Append(",Medicine ").Append(index)
                    .Append(",10,FALSE,Maker,allopathy,pack,Salt (10mg),\r\n");
            }

            await File.WriteAllTextAsync(catalogPath, csv.ToString());
            var importer = new CatalogImportService(database.Context);
            using var cancellation = new CancellationTokenSource();
            EventHandler<CatalogImportProgress> cancelAfterFirstBatch = (_, progress) =>
            {
                if (progress.ImportKey == "catalog" && progress.Status == "Importing")
                {
                    cancellation.Cancel();
                }
            };
            importer.ProgressChanged += cancelAfterFirstBatch;

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                importer.ImportCatalogAsync(catalogPath, cancellation.Token));
            importer.ProgressChanged -= cancelAfterFirstBatch;
            Assert.Equal(1000, await database.Context.CatalogMedicines.CountAsync());

            await importer.ImportCatalogAsync(catalogPath);
            Assert.Equal(1001, await database.Context.CatalogMedicines.CountAsync());
            await importer.ImportCatalogAsync(catalogPath, forceReimport: true);
            Assert.Equal(1001, await database.Context.CatalogMedicines.CountAsync());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Importer_ParsesBundledAndroidCatalogHeadersAndFirstBatches()
    {
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_catalog.csv");
        var infoPath = Path.Combine(AppContext.BaseDirectory, "Data", "medicine_info.csv");
        Assert.True(File.Exists(catalogPath), $"Bundled catalog file was not copied: {catalogPath}");
        Assert.True(File.Exists(infoPath), $"Bundled info file was not copied: {infoPath}");

        await using var database = await DatabaseTestContext.CreateAsync();
        var importer = new CatalogImportService(database.Context);
        using var cancelCatalog = new CancellationTokenSource();
        EventHandler<CatalogImportProgress> stopCatalogAfterFirstBatch = (_, progress) =>
        {
            if (progress.ImportKey == "catalog" && progress.Status == "Importing")
            {
                cancelCatalog.Cancel();
            }
        };
        importer.ProgressChanged += stopCatalogAfterFirstBatch;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            importer.ImportCatalogAsync(catalogPath, cancelCatalog.Token));
        importer.ProgressChanged -= stopCatalogAfterFirstBatch;
        Assert.Equal(1000, await database.Context.CatalogMedicines.CountAsync());
        Assert.Contains(await database.Context.CatalogMedicines.ToListAsync(),
            medicine => medicine.Name == "Augmentin 625 Duo Tablet");

        using var cancelInfo = new CancellationTokenSource();
        EventHandler<CatalogImportProgress> stopInfoAfterFirstBatch = (_, progress) =>
        {
            if (progress.ImportKey == "info" && progress.Status == "Importing")
            {
                cancelInfo.Cancel();
            }
        };
        importer.ProgressChanged += stopInfoAfterFirstBatch;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            importer.ImportInfoAsync(infoPath, cancelInfo.Token));
        importer.ProgressChanged -= stopInfoAfterFirstBatch;
        Assert.Equal(1000, await database.Context.CatalogInfos.CountAsync());
        Assert.Contains(await database.Context.CatalogInfos.ToListAsync(),
            info => info.NameKey == "augmentin 625 duo tablet");
    }
}
