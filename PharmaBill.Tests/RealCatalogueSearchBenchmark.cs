using PharmaBill.Core;
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PharmaBill.Core.Catalog;
using PharmaBill.Data.Persistence;
using PharmaBill.Data.Services;
using Xunit;

namespace PharmaBill.Tests;

/// <summary>
/// Measurement harness against a COPY of the real catalogue database. It does nothing unless
/// PHARMABILL_PERF_DB_DIR points at a folder containing pharmabill.db and data\database.key.
/// Optional: PHARMABILL_PERF_OUT (report path), PHARMABILL_PERF_LABEL (heading).
/// </summary>
[Collection(RunsAlone.Name)]
public sealed class RealCatalogueSearchBenchmark
{
    private static readonly string[] Queries = ["p", "pa", "para", "paracetamol", "alpraz"];

    private const int WarmRuns = 7;

    [Fact]
    public async Task Measure_real_catalogue_search_phases()
    {
        string? directory = Environment.GetEnvironmentVariable("PHARMABILL_PERF_DB_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string output = Environment.GetEnvironmentVariable("PHARMABILL_PERF_OUT") ?? Path.Combine(Path.GetTempPath(), "searchperf.txt");
        string label = Environment.GetEnvironmentVariable("PHARMABILL_PERF_LABEL") ?? "run";
        string databasePath = Path.Combine(directory, "pharmabill.db");
        string hex = Convert.ToHexString(new DatabaseEncryptionKeyProvider(Path.Combine(directory, "data", "database.key")).GetOrCreateKey());
        StringBuilder report = new StringBuilder();
        void Line(string text = "")
        {
            report.AppendLine(text);
            Console.WriteLine(text);
        }

        Line($"=== SearchPerf {label} === {BusinessClock.Now:yyyy-MM-dd HH:mm:ss} Release={!IsDebug()}");
        await using SearchStack stack = SearchStack.Open(databasePath, hex, directory);

        await using (SqliteConnection raw = new SqliteConnection(stack.ConnectionString))
        {
            await raw.OpenAsync();
            foreach (string table in new[] { "CatalogMedicines", "CatalogInfos", "CatalogMedicineFts", "Drugs", "Batches", "StockMovements" })
            {
                Line($"rows {table,-20} {await Scalar(raw, $"SELECT COUNT(*) FROM \"{table}\"")}");
            }
        }

        await MeasureConnectionOpenAsync(stack, (text) => Line(text));

        Line();
        Line("--- Pipeline A: CatalogSearchService.SearchAsync (global search box) ---");
        Line("query        run   total  syncPart   dbQuery   convert  inStock catalog");
        long firstEver = Stopwatch.GetTimestamp();
        foreach (string query in Queries)
        {
            List<(double Total, double Sync, double Db, double Convert)> warm = [];
            for (int run = 0; run <= WarmRuns; run++)
            {
                using IServiceScope scope = stack.Scopes.CreateScope();
                CatalogSearchService service = scope.ServiceProvider.GetRequiredService<CatalogSearchService>();
                SearchPerfTrace trace = new SearchPerfTrace("A", run, query);
                long start = Stopwatch.GetTimestamp();
                Task<MedicineSearchResults> task = service.SearchAsync(query, CancellationToken.None, trace);
                double syncPart = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                MedicineSearchResults results = await task;
                double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (run == 0)
                {
                    Line($"{query,-12} first {total,7:N1} {syncPart,9:N1} {trace.DbQueryMs,9:N1} {trace.ConvertMs,9:N1} {results.InStock.Count,8} {results.FromCatalog.Count,7}");
                }
                else
                {
                    warm.Add((total, syncPart, trace.DbQueryMs, trace.ConvertMs));
                }
            }

            (double Total, double Sync, double Db, double Convert) median = Median(warm);
            Line($"{query,-12} warm  {median.Total,7:N1} {median.Sync,9:N1} {median.Db,9:N1} {median.Convert,9:N1}   (median of {WarmRuns}, max total {warm.Max(item => item.Total):N1})");
        }

        _ = firstEver;
        Line();
        Line("--- Pipeline B: RetailBillingService.SearchStockAsync (billing search box) ---");
        Line("query        run   total  syncPart  slowest DB commands (ms)");
        foreach (string query in Queries)
        {
            List<(double Total, double Sync)> warm = [];
            for (int run = 0; run <= WarmRuns; run++)
            {
                using IServiceScope scope = stack.Scopes.CreateScope();
                RetailBillingService service = scope.ServiceProvider.GetRequiredService<RetailBillingService>();
                while (stack.Timing.Commands.TryDequeue(out _))
                {
                }

                long start = Stopwatch.GetTimestamp();
                Task<IReadOnlyList<PharmaBill.Data.Services.RetailStockChoice>> task = service.SearchStockAsync(query);
                double syncPart = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                IReadOnlyList<PharmaBill.Data.Services.RetailStockChoice> choices = await task;
                double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                (string Sql, double Ms)[] commands = stack.Timing.Commands.ToArray();
                if (run == 0 || run == 3)
                {
                    string top = string.Join(" | ", commands.OrderByDescending(item => item.Ms).Take(3).Select(item => $"{item.Ms:N0}ms {Shorten(item.Sql)}"));
                    Line($"{query,-12} {(run == 0 ? "first" : "warm"),-5} {total,7:N1} {syncPart,9:N1}  cmds={commands.Length} choices={choices.Count}  {top}");
                }

                if (run > 0)
                {
                    warm.Add((total, syncPart));
                }
            }

            Line($"{query,-12} median {warm.Select(item => item.Total).Order().ElementAt(WarmRuns / 2),7:N1} {warm.Select(item => item.Sync).Order().ElementAt(WarmRuns / 2),9:N1}   (max total {warm.Max(item => item.Total):N1})");
        }

        Line();
        Line("--- EXPLAIN QUERY PLAN ---");
        await using (SqliteConnection raw = new SqliteConnection(stack.ConnectionString))
        {
            await raw.OpenAsync();
            string[] ids = await Strings(raw, "SELECT Id FROM CatalogMedicines LIMIT 3");
            string idList = string.Join(",", ids.Select(id => $"'{id}'"));
            (string Name, string Sql)[] statements =
            [
                ("FTS prefix", "SELECT MedicineId FROM CatalogMedicineFts WHERE CatalogMedicineFts MATCH '\"para\"*' LIMIT 200"),
                ("custom LIKE", "SELECT Id FROM CatalogMedicines WHERE IsDiscontinued = 0 AND IsCustom = 1 AND (Name LIKE '%para%' OR IFNULL(GenericName,'') LIKE '%para%' OR IFNULL(BrandName,'') LIKE '%para%' OR IFNULL(ShortComposition1,'') LIKE '%para%' OR IFNULL(Manufacturer,'') LIKE '%para%') LIMIT 40"),
                ("drugs LIKE", "SELECT Id FROM Drugs WHERE IsActive = 1 AND (Name LIKE '%para%' OR IFNULL(GenericName,'') LIKE '%para%' OR IFNULL(BrandName,'') LIKE '%para%') ORDER BY Name LIMIT 40"),
                ("medicines by id", $"SELECT * FROM CatalogMedicines WHERE Id IN ({idList}) AND IsDiscontinued = 0"),
                ("habit lookup", "SELECT NameKey FROM CatalogInfos WHERE IsHabitForming = 1 AND NameKey IN ('a','b','c')"),
                ("retail info lookup (OR)", $"SELECT * FROM CatalogInfos WHERE IsDeleted = 0 AND ((CatalogMedicineId IS NOT NULL AND CatalogMedicineId IN ({idList})) OR NameKey IN ('a','b'))")
            ];
            foreach ((string name, string sql) in statements)
            {
                Line($"[{name}]");
                await using SqliteCommand command = raw.CreateCommand();
                command.CommandText = "EXPLAIN QUERY PLAN " + sql;
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    Line("   " + reader.GetString(3));
                }
            }
        }

        await File.WriteAllTextAsync(output, report.ToString());
    }

    private static async Task MeasureConnectionOpenAsync(SearchStack stack, Action<string> line)
    {
        line(string.Empty);
        line("--- SQLCipher connection open (open + SELECT 1) ---");
        List<double> cold = [];
        for (int i = 0; i < 4; i++)
        {
            SqliteConnection.ClearAllPools();
            cold.Add(await TimeOpenAsync(stack.ConnectionString));
        }

        line($"cold (pool cleared, key derived)   : {string.Join(", ", cold.Select(value => value.ToString("N1")))} ms  (avg {cold.Average():N1})");
        await TimeOpenAsync(stack.ConnectionString);
        List<double> pooled = [];
        for (int i = 0; i < 10; i++)
        {
            pooled.Add(await TimeOpenAsync(stack.ConnectionString));
        }

        line($"pooled (reuse within seconds)      : avg {pooled.Average():N2} ms, max {pooled.Max():N2} ms");
        await Task.Delay(TimeSpan.FromSeconds(16));
        double afterIdle = await TimeOpenAsync(stack.ConnectionString);
        line($"after 16 s idle (pool pruned?)     : {afterIdle:N1} ms");
    }

    private static async Task<double> TimeOpenAsync(string connectionString)
    {
        long start = Stopwatch.GetTimestamp();
        await using SqliteConnection connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static (double Total, double Sync, double Db, double Convert) Median(List<(double Total, double Sync, double Db, double Convert)> values) =>
        (values.Select(item => item.Total).Order().ElementAt(values.Count / 2),
         values.Select(item => item.Sync).Order().ElementAt(values.Count / 2),
         values.Select(item => item.Db).Order().ElementAt(values.Count / 2),
         values.Select(item => item.Convert).Order().ElementAt(values.Count / 2));

    private static string Shorten(string sql) => sql.Length <= 90 ? sql : sql[..90] + "...";

    private static async Task<string> Scalar(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static async Task<string[]> Strings(SqliteConnection connection, string sql)
    {
        List<string> values = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return [.. values];
    }

    private static bool IsDebug() =>
#if DEBUG
        true;
#else
        false;
#endif
}
