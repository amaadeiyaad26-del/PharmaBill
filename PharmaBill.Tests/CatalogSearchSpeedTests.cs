using System.Diagnostics;
using System.Text;
using PharmaBill.Core.Catalog;
using PharmaBill.Data.Services;
using Xunit;
using Xunit.Abstractions;

namespace PharmaBill.Tests;

// Search speed on a catalogue the size of a shop's drug bank, imported the way the app imports it. Timed warm (the
// median of repeated searches), because what a cashier feels is every search after the first, not JIT and query
// compilation on the first one. Runs alone: a timing only means something when 500 other tests aren't sharing the CPU
// (in parallel the same search measured 121 ms instead of 30 ms).
[Collection(RunsAlone.Name)]
public sealed class CatalogSearchSpeedTests(ITestOutputHelper output)
{
    // Imported through the real importer, then doubled six times inside SQLite: 1,563 x 64 = 100,032 medicines.
    // (Importing 100,000 rows one batch at a time takes over a minute; the doubled rows are copies of imported ones.)
    private const int Imported = 1_563;

    private const int Doublings = 6;

    private static readonly string[] Stems =
    [
        "Paracet", "Amoxy", "Azithro", "Cefix", "Pantop", "Omepra", "Metfor", "Glimep", "Atorva", "Rosuva", "Amlodi", "Telmi",
        "Losar", "Cetiri", "Levoceti", "Montel", "Dolo", "Ibupro", "Diclo", "Aceclo", "Alpraz", "Clonaz", "Sertra", "Escitalo",
    ];

    [Fact]
    public async Task WarmSearch_OnAHundredThousandMedicines_UsesTheIndexAndStaysQuick()
    {
        await using var database = await DatabaseTestContext.CreateAsync();
        string directory = Path.Combine(Path.GetTempPath(), $"PharmaBill.CatalogSpeed.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string catalogPath = Path.Combine(directory, "medicine_catalog.csv");
            string infoPath = Path.Combine(directory, "medicine_info.csv");
            var catalog = new StringBuilder("id,name,price(₹),Is_discontinued,manufacturer_name,type,pack_size_label,short_composition1,short_composition2\r\n");
            for (int i = 1; i <= Imported; i++)
            {
                string stem = Stems[i % Stems.Length];
                catalog.Append(i).Append(',').Append(stem).Append("ol ").Append(i % 997).Append(" Tablet,")
                    .Append(10 + i % 300).Append(".50,FALSE,Maker ").Append(i % 211).Append(",allopathy,strip of 10,")
                    .Append(stem).Append("amine (").Append(50 * (1 + i % 12)).Append("mg),\r\n");
            }
            await File.WriteAllTextAsync(catalogPath, catalog.ToString(), new UTF8Encoding(true));
            await File.WriteAllTextAsync(infoPath, "name_key,habit_forming,therapeutic_class,chemical_class,action_class,use\r\n", new UTF8Encoding(true));
            var import = Stopwatch.StartNew();
            await new CatalogImportService(database.Context).ImportAllAsync(catalogPath, infoPath);
            int medicines = await GrowCatalogueAsync(database);
            output.WriteLine($"built a catalogue of {medicines:N0} medicines in {import.Elapsed.TotalSeconds:0.0} s");

            foreach (string query in new[] { "para", "paracetol", "amoxy", "alpraz 5", "maker 7" })
            {
                var warm = new List<double>();
                for (int run = 0; run <= 9; run++)
                {
                    var trace = new SearchPerfTrace("speed", run, query);
                    var watch = Stopwatch.StartNew();
                    MedicineSearchResults results = await database.CreateCatalogSearch().SearchAsync(query, CancellationToken.None, trace);
                    double ms = watch.Elapsed.TotalMilliseconds;
                    Assert.NotEmpty(results.FromCatalog);
                    Assert.Equal(SearchPerfTrace.FullTextIndex, trace.CatalogLookup);
                    if (run > 0)
                    {
                        warm.Add(ms);
                    }
                }
                double median = warm.Order().ElementAt(warm.Count / 2);
                output.WriteLine($"\"{query}\": warm median {median:0.0} ms, slowest {warm.Max():0.0} ms");
                Assert.True(median < 100, $"warm search for \"{query}\" took {median:0} ms (median of {warm.Count}) on {medicines:N0} medicines");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<int> GrowCatalogueAsync(DatabaseTestContext database)
    {
        var columns = new List<string>();
        await using (var info = database.Connection.CreateCommand())
        {
            info.CommandText = "PRAGMA table_info(CatalogMedicines)";
            await using var reader = await info.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }
        }
        string list = string.Join(", ", columns.Select(c => $"\"{c}\""));
        for (int generation = 1; generation <= Doublings; generation++)
        {
            string copy = string.Join(", ", columns.Select(c => c switch
            {
                "Id" => "upper(substr(h, 1, 8) || '-' || substr(h, 9, 4) || '-' || substr(h, 13, 4) || '-' || substr(h, 17, 4) || '-' || substr(h, 21))",
                "SourceId" => $"\"SourceId\" || '.{generation}'",
                _ => $"\"{c}\"",
            }));
            await Execute(database, $"INSERT INTO CatalogMedicines ({list}) SELECT {copy} FROM (SELECT *, hex(randomblob(16)) AS h FROM CatalogMedicines)");
        }
        // The index rows, as the importer writes them (RefreshFtsRowsAsync): id without dashes, name, compositions, maker.
        await Execute(database, "DELETE FROM CatalogMedicineFts");
        await Execute(database, "INSERT INTO CatalogMedicineFts (MedicineId, Name, Composition, Manufacturer) "
            + "SELECT lower(replace(Id, '-', '')), Name, trim(coalesce(ShortComposition1, '') || ' ' || coalesce(ShortComposition2, '')), coalesce(Manufacturer, '') FROM CatalogMedicines");
        await using var count = database.Connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM CatalogMedicineFts";
        return Convert.ToInt32(await count.ExecuteScalarAsync());
    }

    private static async Task Execute(DatabaseTestContext database, string sql)
    {
        await using var command = database.Connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
