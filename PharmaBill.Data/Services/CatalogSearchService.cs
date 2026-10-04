using System.Data;
using Microsoft.EntityFrameworkCore;
using PharmaBill.Core.Catalog;
using PharmaBill.Core.Entities;
using PharmaBill.Data.Persistence;

namespace PharmaBill.Data.Services;

public sealed record MedicineSearchResult(
    Guid CatalogMedicineId,
    string Name,
    string? Composition,
    string CompositionKey,
    string? Manufacturer,
    string? PackSize,
    decimal? ReferencePrice,
    bool IsHabitForming,
    decimal StockQuantity,
    bool IsInStock,
    DateOnly? NearestExpiry = null);

public sealed class MedicineSearchResults(
    IReadOnlyList<MedicineSearchResult> inStock,
    IReadOnlyList<MedicineSearchResult> fromCatalog)
{
    public IReadOnlyList<MedicineSearchResult> InStock { get; } = inStock;

    public IReadOnlyList<MedicineSearchResult> FromCatalog { get; } = fromCatalog;
}

public sealed class CatalogSearchService(PharmaBillDbContext context)
{
    public const int MaxResultsPerGroup = 25;

    public async Task<MedicineSearchResults> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Trim().Length < 2)
        {
            return new MedicineSearchResults([], []);
        }

        var ftsQuery = BuildFtsQuery(query);
        if (ftsQuery.Length == 0)
        {
            return new MedicineSearchResults([], []);
        }

        var matchingIds = await FindCatalogIdsAsync(ftsQuery, cancellationToken);
        if (matchingIds.Count == 0)
        {
            return new MedicineSearchResults([], []);
        }

        var medicines = await context.CatalogMedicines
            .AsNoTracking()
            .Where(medicine => matchingIds.Contains(medicine.Id) && !medicine.IsDiscontinued)
            .ToListAsync(cancellationToken);
        var localNames = medicines.Select(medicine => medicine.Name.ToLower()).Distinct().ToArray();
        var habits = await context.CatalogInfos.AsNoTracking()
            .Where(info => info.IsHabitForming && localNames.Contains(info.NameKey))
            .Select(info => info.NameKey)
            .ToHashSetAsync(cancellationToken);

        var drugLinks = await context.Drugs.AsNoTracking()
            .Where(drug => drug.CatalogMedicineId.HasValue &&
                           matchingIds.Contains(drug.CatalogMedicineId.Value) &&
                           drug.IsActive)
            .Select(drug => new { drug.Id, drug.CatalogMedicineId, drug.Name })
            .ToListAsync(cancellationToken);
        var quantities = await context.Batches.AsNoTracking()
            .Where(batch => drugLinks.Select(link => link.Id).Contains(batch.DrugId))
            .GroupBy(batch => batch.DrugId)
            .Select(group => new { DrugId = group.Key, Quantity = group.Sum(batch => batch.Quantity) })
            .ToDictionaryAsync(item => item.DrugId, item => item.Quantity, cancellationToken);
        var stockByCatalogId = drugLinks
            .Where(link => quantities.GetValueOrDefault(link.Id) > 0)
            .GroupBy(link => link.CatalogMedicineId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(link => quantities.GetValueOrDefault(link.Id)));
        var expiries = await context.Batches.AsNoTracking()
            .Where(batch => drugLinks.Select(link => link.Id).Contains(batch.DrugId) &&
                            batch.Quantity > 0 && batch.ExpiryDate != null)
            .Select(batch => new { batch.DrugId, Expiry = batch.ExpiryDate!.Value })
            .ToListAsync(cancellationToken);
        var expiryByCatalogId = drugLinks
            .SelectMany(link => expiries.Where(item => item.DrugId == link.Id)
                .Select(item => (CatalogId: link.CatalogMedicineId!.Value, item.Expiry)))
            .GroupBy(item => item.CatalogId)
            .ToDictionary(group => group.Key, group => (DateOnly?)group.Min(item => item.Expiry));
        var linkedCatalogIds = drugLinks
            .Where(link => quantities.GetValueOrDefault(link.Id) > 0)
            .Select(link => link.CatalogMedicineId!.Value)
            .ToHashSet();

        var results = medicines
            .OrderBy(medicine => matchingIds.IndexOf(medicine.Id))
            .Select(medicine => ToResult(
                medicine,
                habits.Contains(medicine.Name.ToLowerInvariant()),
                stockByCatalogId.GetValueOrDefault(medicine.Id),
                expiryByCatalogId.GetValueOrDefault(medicine.Id)))
            .ToArray();
        return new MedicineSearchResults(
            results.Where(result => result.IsInStock).Take(MaxResultsPerGroup).ToArray(),
            results.Where(result => !linkedCatalogIds.Contains(result.CatalogMedicineId))
                .Take(MaxResultsPerGroup)
                .ToArray());
    }

    public async Task<IReadOnlyList<MedicineSearchResult>> FindSubstitutesAsync(
        string compositionKey,
        Guid excludeCatalogMedicineId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compositionKey);
        var candidates = await context.CatalogMedicines.AsNoTracking()
            .Where(medicine => medicine.CompositionKey == compositionKey &&
                               medicine.Id != excludeCatalogMedicineId &&
                               !medicine.IsDiscontinued)
            .OrderBy(medicine => medicine.ReferencePrice ?? decimal.MaxValue)
            .Take(100)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
        {
            return [];
        }

        var ids = candidates.Select(item => item.Id).ToArray();
        var localLinks = await context.Drugs.AsNoTracking()
            .Where(drug => drug.CatalogMedicineId.HasValue && ids.Contains(drug.CatalogMedicineId.Value))
            .Select(drug => new { drug.Id, CatalogId = drug.CatalogMedicineId!.Value })
            .ToListAsync(cancellationToken);
        var quantities = await context.Batches.AsNoTracking()
            .Where(batch => localLinks.Select(link => link.Id).Contains(batch.DrugId))
            .GroupBy(batch => batch.DrugId)
            .Select(group => new { DrugId = group.Key, Quantity = group.Sum(batch => batch.Quantity) })
            .ToDictionaryAsync(item => item.DrugId, item => item.Quantity, cancellationToken);
        var stock = localLinks
            .Where(link => quantities.GetValueOrDefault(link.Id) > 0)
            .GroupBy(link => link.CatalogId)
            .ToDictionary(group => group.Key, group => group.Sum(link => quantities.GetValueOrDefault(link.Id)));
        var names = candidates.Select(item => item.Name.ToLowerInvariant()).Distinct().ToArray();
        var habits = await context.CatalogInfos.AsNoTracking()
            .Where(info => info.IsHabitForming && names.Contains(info.NameKey))
            .Select(info => info.NameKey)
            .ToHashSetAsync(cancellationToken);

        return candidates
            .OrderByDescending(item => stock.ContainsKey(item.Id))
            .ThenBy(item => item.ReferencePrice ?? decimal.MaxValue)
            .Take(MaxResultsPerGroup)
            .Select(item => ToResult(item, habits.Contains(item.Name.ToLowerInvariant()), stock.GetValueOrDefault(item.Id)))
            .ToArray();
    }

    private async Task<List<Guid>> FindCatalogIdsAsync(string ftsQuery, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT MedicineId
                FROM CatalogMedicineFts
                WHERE CatalogMedicineFts MATCH $query
                LIMIT 200
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$query";
            parameter.Value = ftsQuery;
            command.Parameters.Add(parameter);
            var result = new List<Guid>(200);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (Guid.TryParseExact(reader.GetString(0), "N", out var id))
                {
                    result.Add(id);
                }
            }

            return result;
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string BuildFtsQuery(string query)
    {
        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => new string(token.Where(char.IsLetterOrDigit).ToArray()))
            .Where(token => token.Length > 0)
            .Take(5)
            .Select(token => $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\"*")
            .ToArray();
        return string.Join(" AND ", tokens);
    }

    private static MedicineSearchResult ToResult(
        CatalogMedicine medicine,
        bool habitForming,
        decimal stockQuantity,
        DateOnly? nearestExpiry = null) =>
        new(
            medicine.Id,
            medicine.Name,
            JoinComposition(medicine.ShortComposition1, medicine.ShortComposition2),
            medicine.CompositionKey,
            medicine.Manufacturer,
            medicine.PackSizeLabel,
            medicine.ReferencePrice,
            habitForming,
            stockQuantity,
            stockQuantity > 0,
            nearestExpiry);

    private static string? JoinComposition(string? first, string? second)
    {
        var values = new[] { first, second }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return values.Length == 0 ? null : string.Join(", ", values);
    }
}
